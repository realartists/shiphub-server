namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Threading;
  using System.Threading.Tasks;
  using Common;
  using Common.GitHub;
  using Logging;
  using Microsoft.WindowsAzure.Storage;
  using RealArtists.ShipHub.Common.GitHub.Models;

  public interface IGitHubClient {
    string AccessToken { get; }
    Uri ApiRoot { get; }
    ProductInfoHeaderValue UserAgent { get; }
    string UserInfo { get; }
    long UserId { get; }

    int NextRequestId();
  }

  /// <summary>
  /// Currently supports redirects, pagination, rate limits and limited retry logic.
  /// 
  /// WARNING! THIS CLASS DOES NO RATE LIMIT ENFORCEMENT OR TRACKING
  /// ALL ACCOUNTING SHOULD LIVE IN CALLING CODE
  /// </summary>
  public class GitHubClient {
    public const int LastAttempt = 2; // Make three attempts
    public const int RetryMilliseconds = 1000;

    private static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    private static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
    public static ProductInfoHeaderValue UserAgent { get; } = new ProductInfoHeaderValue(ApplicationName, ApplicationVersion);

    private static readonly HttpClient _HttpClient = CreateGitHubHttpClient();

    private Uri _apiRoot;

    public GitHubClient(Uri apiRoot, string debugInfo, IEnumerable<string> tokens) {
      _apiRoot = apiRoot;
    }

    private static void EnsureHandlerPipelineCreated(Uri apiRoot) {
      if (SharedHandler != null) {
        return;
      }

      // Set the maximum number of concurrent connections
      HttpUtilities.SetServicePointConnectionLimit(apiRoot);

      SharedHandler = new GitHubHandler();
    }

    public async Task<GitHubResponse<T>> Fetch<T>(GitHubRequest request, CancellationToken cancellationToken) {
      GitHubResponse<T> result = null;

      for (var attempt = 0; attempt <= LastAttempt; ++attempt) {
        cancellationToken.ThrowIfCancellationRequested();

        if (attempt > 0) {
          await Task.Delay(RetryMilliseconds * attempt);
        }

        try {
          result = await MakeRequest<T>(request, cancellationToken, null);
        } catch (HttpRequestException hre) {
          if (attempt < LastAttempt) {
            hre.Report($"Error making GitHub request: {request.Uri}");
            continue;
          }
          throw;
        }

        switch (result.Status) {
          case HttpStatusCode.BadGateway:
          case HttpStatusCode.GatewayTimeout:
          case HttpStatusCode.InternalServerError:
          case HttpStatusCode.ServiceUnavailable:
            continue; // retry after delay
          default:
            break; // switch
        }

        break; //for
      }

      return result;
    }

    private async Task<GitHubResponse<T>> MakeRequest<T>(GitHubRequest request, CancellationToken cancellationToken, GitHubRedirect redirect) {
      var uri = new Uri(_apiRoot, request.Uri);
      var httpRequest = new HttpRequestMessage(request.Method, uri) {
        Content = request.CreateBodyContent(),
      };

      // Accept header
      if (!request.AcceptHeaderOverride.IsNullOrWhiteSpace()) {
        httpRequest.Headers.Accept.Clear();
        httpRequest.Headers.Accept.ParseAdd(request.AcceptHeaderOverride);
      } else if (typeof(T) == typeof(byte[])) {
        httpRequest.Headers.Accept.Clear();
        httpRequest.Headers.Accept.ParseAdd("application/vnd.github.v3.raw");
      }

      httpRequest.Headers.Authorization = new AuthenticationHeaderValue("bearer", client.AccessToken);

      var cache = request.CacheOptions;
      if (cache?.UserId == client.UserId) {
        if (request.Method != HttpMethod.Get) {
          throw new InvalidOperationException("Cache options are only valid on GET requests.");
        }

        httpRequest.Headers.IfModifiedSince = cache.LastModified;
        if (!cache.ETag.IsNullOrWhiteSpace()) {
          httpRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cache.ETag));
        }
      }

      // User agent
      httpRequest.Headers.UserAgent.Clear();
      httpRequest.Headers.UserAgent.Add(client.UserAgent);

      // For logging
      LoggingMessageProcessingHandler.SetLogDetails(
        httpRequest,
        client.UserInfo,
        $"{client.UserInfo}/{DateTime.UtcNow:o}_{client.NextRequestId()}{httpRequest.RequestUri.PathAndQuery.Replace('/', '_')}.txt",
        request.CreationDate
      );

      HttpResponseMessage response;
      var sw = Stopwatch.StartNew();
      try {
        response = await _HttpClient.SendAsync(httpRequest, cancellationToken);
      } catch (TaskCanceledException exception) {
        sw.Stop();
        exception.Report($"Request aborted for /{request.Uri} after {sw.ElapsedMilliseconds} msec [{LoggingMessageProcessingHandler.ExtractString(httpRequest, LoggingMessageProcessingHandler.LogBlobNameKey)}]");
        throw;
      }

      // Handle redirects
      switch (response.StatusCode) {
        case HttpStatusCode.MovedPermanently:
        case HttpStatusCode.RedirectKeepVerb:
          request = request.CloneWithNewUri(response.Headers.Location);
          return await MakeRequest<T>(client, request, cancellationToken, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        case HttpStatusCode.Redirect:
        case HttpStatusCode.RedirectMethod:
          request = request.CloneWithNewUri(response.Headers.Location);
          request.Method = HttpMethod.Get;
          return await MakeRequest<T>(client, request, cancellationToken, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        default:
          break;
      }

      var result = new GitHubResponse<T>(request) {
        Date = response.Headers.Date.Value,
        Redirect = redirect,
        Status = response.StatusCode,
      };

      // Cache Headers
      result.CacheData = new GitHubCacheDetails() {
        UserId = client.UserId,
        Path = request.Path,
        AccessToken = httpRequest.Headers.Authorization.Parameter,
        ETag = response.Headers.ETag?.Tag,
        LastModified = response.Content?.Headers?.LastModified,
        PollInterval = response.ParseHeader("X-Poll-Interval", x => (x == null) ? TimeSpan.Zero : TimeSpan.FromSeconds(int.Parse(x))),
      };

      // Expires and Caching Max-Age
      var expires = response.Content?.Headers?.Expires;
      var maxAgeSpan = response.Headers.CacheControl?.SharedMaxAge ?? response.Headers.CacheControl?.MaxAge;
      if (maxAgeSpan != null) {
        var maxAgeExpires = DateTimeOffset.UtcNow.Add(maxAgeSpan.Value);
        if (expires == null || maxAgeExpires < expires) {
          expires = maxAgeExpires;
        }
      }
      result.CacheData.Expires = expires;

      // Experiment:
      var pollExpires = result.Date.Add(result.CacheData.PollInterval);
      if (result.CacheData.Expires > pollExpires) {
        result.CacheData.Expires = pollExpires;
      }

      // Rate Limits
      // These aren't always sent. Check for presence and fail gracefully.
      if (response.Headers.Contains("X-RateLimit-Limit")) {
        result.RateLimit = new GitHubRateLimit(
          client.AccessToken,
          response.ParseHeader("X-RateLimit-Limit", x => int.Parse(x)),
          response.ParseHeader("X-RateLimit-Remaining", x => int.Parse(x)),
          response.ParseHeader("X-RateLimit-Reset", x => EpochUtility.ToDateTimeOffset(int.Parse(x))));
      }

      // Abuse
      if (response.Headers.RetryAfter != null) {
        var after = response.Headers.RetryAfter;
        if (after.Date != null) {
          result.RetryAfter = after.Date;
        } else if (after.Delta != null) {
          result.RetryAfter = DateTimeOffset.UtcNow.Add(after.Delta.Value);
        }
      }

      // Scopes
      var scopes = response.ParseHeader<IEnumerable<string>>("X-OAuth-Scopes", x => x?.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
      if (scopes != null) {
        result.Scopes.UnionWith(scopes);
      }

      // Pagination
      // Screw the RFC, minimally match what GitHub actually sends.
      result.Pagination = response.ParseHeader("Link", x => (x == null) ? null : GitHubPagination.FromLinkHeader(x));

      if (result.Succeeded) {
        if (response.StatusCode == HttpStatusCode.NoContent && typeof(T) == typeof(bool)) {
          // Gross special case hack for Assignable :/
          result.Result = (T)(object)true;
        } else if (response.Content != null && typeof(T) == typeof(byte[])) {
          // Raw byte result
          result.Result = (T)(object)(await response.Content.ReadAsByteArrayAsync());
        } else if (response.Content != null && request is GitHubGraphQLRequest) {
          // GraphQL
          var resp = await response.Content.ReadAsAsync<GraphQLResponse<T>>(GraphQLSerialization.MediaTypeFormatters);
          result.Result = resp.Data;

          // Possible to get errors and a (partially) useful response.
          if (resp.Errors?.Any() == true) {
            result.Error = new GitHubError() {
              // This is a gross hack.
              Message = resp.Errors.SerializeObject(),
            };
          }
        } else if (response.Content != null) {
          // JSON formatted result
          result.Result = await response.Content.ReadAsAsync<T>(GitHubSerialization.MediaTypeFormatters);
          // At this point each response represents a single page.
          result.Pages = 1;
        }
      } else if (response.Content != null) {
        var mediaType = response.Content.Headers.ContentType.MediaType;
        if (mediaType.Contains("github") || mediaType.Contains("json")) {
          result.Error = await response.Content.ReadAsAsync<GitHubError>(GitHubSerialization.MediaTypeFormatters);
          result.Error.Status = response.StatusCode;
        } else {
          // So far, these have all been nginx errors, mostly unicorns and 502s
          // They're already logged by the LoggingMessageProcessingHandler.
          var body = await response.Content.ReadAsStringAsync();
          Log.Info($"Invalid GitHub Response for [{request.Uri}]:\n\n{body}");
        }
      }

      return result;
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    private static HttpClient CreateGitHubHttpClient() {
#if DEBUG
      var handler = HttpUtilities.CreateDefaultHandler(ShipHubCloudConfiguration.Instance.UseFiddler);
#else
      var handler = HttpUtilities.CreateDefaultHandler();
#endif

      // TODO: Inject this or something.
      // Always enable even if not storing bodies for generic request logging.
      var gitHubLoggingStorage = ShipHubCloudConfiguration.Instance.GitHubLoggingStorage;
      handler = new LoggingMessageProcessingHandler(
        gitHubLoggingStorage.IsNullOrWhiteSpace() ? null : CloudStorageAccount.Parse(gitHubLoggingStorage),
        "github-logs2",
        handler
      );

      var httpClient = new HttpClient(handler, true) {
        // TODO: Decouple this
        Timeout = GitHubActor.GitHubRequestTimeout,
      };

      var headers = httpClient.DefaultRequestHeaders;
      headers.AcceptEncoding.Clear();
      headers.AcceptEncoding.ParseAdd("gzip");
      headers.AcceptEncoding.ParseAdd("deflate");

      headers.Accept.Clear();
      headers.Accept.ParseAdd("application/vnd.github.v3+json");

      headers.AcceptCharset.Clear();
      headers.AcceptCharset.ParseAdd("utf-8");

      headers.Add("Time-Zone", "Etc/UTC");

      return httpClient;
    }

    private async Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubRequest request, Func<T, TKey> keySelector, uint softPageLimit = uint.MaxValue, uint skipPages = 0, uint hardPageLimit = uint.MaxValue) {
      if (request.Method != HttpMethod.Get) {
        throw new InvalidOperationException("Only GETs can be paginated.");
      }
      if (softPageLimit == 0) {
        throw new InvalidOperationException($"{nameof(softPageLimit)} must be omitted or greater than 0");
      }
      if (hardPageLimit == 0) {
        throw new InvalidOperationException($"{nameof(hardPageLimit)} must be omitted or greater than 0");
      }

      // Always request the largest page size
      if (!request.Parameters.ContainsKey("per_page")) {
        request.AddParameter("per_page", PageSize);
      }

      // In all cases we need the first page 🙄
      var response = await EnqueueRequest<IEnumerable<T>>(request);

      // Save first page cache data
      var dangerousFirstPageCacheData = response.CacheData;

      // When successful, try to enumerate. Else immediately return the error.
      if (response.IsOk) {
        // If skipping pages, calculate here.
        switch (skipPages) {
          case 0:
            break;
          case 1 when response.Pagination?.Next != null:
            var nextUri = response.Pagination.Next;
            response = await EnqueueRequest<IEnumerable<T>>(response.Request.CloneWithNewUri(nextUri));
            break;
          case 1: // response.Pagination == null
            response.Result = Array.Empty<T>();
            break;
          default: // skipPages > 1
            if (response.Pagination?.CanInterpolate != true) {
              throw new InvalidOperationException($"Skipping pages is not supported for [{response.Request.Uri}]: {response.Pagination?.SerializeObject()}");
            }
            nextUri = response.Pagination.Interpolate().Skip((int)(skipPages - 1)).FirstOrDefault();
            if (nextUri == null) {
              // We skipped more pages than existed.
              response.Pagination = null;
              response.Result = Array.Empty<T>();
            } else {
              response = await EnqueueRequest<IEnumerable<T>>(response.Request.CloneWithNewUri(nextUri));
            }
            break;
        }

        // Check hard limit
        if (response.Pagination?.CanInterpolate == true
          && response.Pagination.Interpolate().Count() > hardPageLimit) {
          // We'll hit our hard limit, so return no results.
          response.Pagination = null;
          response.Result = Array.Empty<T>();

          // Maybe this is a bad idea, but the goal with the hard limit is to cache the empty result we'll never be able to really enumerate.
          response.CacheData = dangerousFirstPageCacheData;
        } else if (softPageLimit > 1 && response.Pagination?.Next != null) {
          // Now, if there's more to do, enumerate the results
          // By default, upgrade background => subrequest
          var subRequestPriority = RequestPriority.SubRequest;
          // Ensure interactive => interactive
          if (response.Request.Priority == RequestPriority.Interactive) {
            subRequestPriority = RequestPriority.Interactive;
          }

          // Walk in order
          response = await EnumerateSequential(response, subRequestPriority, softPageLimit);
        }
      }

      // Response should have:
      // 1) Pagination header from last page
      // 2) Cache data from first page, IIF it's a complete result, and not truncated due to errors.
      // 3) Number of pages returned

      // Set first page cache data
      response.DangerousFirstPageCacheData = dangerousFirstPageCacheData;

      return response.Distinct(keySelector);
    }

    private async Task<GitHubResponse<IEnumerable<TItem>>> EnumerateSequential<TItem>(GitHubResponse<IEnumerable<TItem>> firstPage, RequestPriority priority, uint maxPages) {
      var partial = false;
      var results = new List<TItem>(firstPage.Result);

      // Walks pages in order, one at a time.
      var current = firstPage;
      uint page = 1;

      while (current.Pagination?.Next != null) {
        if (page >= maxPages) {
          partial = true;
          break;
        }

        var nextReq = current.Request.CloneWithNewUri(current.Pagination.Next);
        nextReq.Priority = priority;
        current = await EnqueueRequest<IEnumerable<TItem>>(nextReq);

        if (current.IsOk) {
          results.AddRange(current.Result);
        } else if (maxPages < uint.MaxValue) {
          // Return results up to this point.
          partial = true;
          break;
        } else {
          // On error, return the error
          return current;
        }

        ++page;
      }

      // Keep cache from the first page, rate + pagination from the last.
      var result = firstPage;
      result.Result = results;
      result.Pages = page;
      result.RateLimit = current.RateLimit;

      // Don't return cache data for partial results
      if (partial) {
        result.CacheData = null;
      }

      return result;
    }
  }
}
