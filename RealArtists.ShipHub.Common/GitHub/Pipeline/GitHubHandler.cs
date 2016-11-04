namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Threading.Tasks;
  using Logging;
  using Microsoft.Azure;
  using Microsoft.WindowsAzure.Storage;

  public interface IGitHubHandler {
    Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request);
    Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector);
  }

  /// <summary>
  /// This is the core GitHub handler. It's the end of the line and does not delegate.
  /// Currently supports redirects, pagination, rate limits and limited retry logic.
  /// </summary>
  public class GitHubHandler : IGitHubHandler {
    public const int MaxRetries = 2;

    public static HttpClient HttpClient { get; } = CreateGitHubHttpClient();
    public static int RetryMilliseconds { get; } = 1000;

    public uint RateLimitFloor { get; set; }

    public GitHubHandler() : this(500) { }
    public GitHubHandler(uint rateLimitFloor) {
      RateLimitFloor = rateLimitFloor;
    }

    public async Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request) {
      client.RateLimit.ThrowIfUnder(RateLimitFloor, client.UserInfo);

      GitHubResponse<T> result = null;

      for (int i = 0; i <= MaxRetries; ++i) {
        result = await MakeRequest<T>(client, request, null);

        if (result.RateLimit != null) {
          client.UpdateInternalRateLimit(result.RateLimit);
        }

        if (!result.IsError) {
          // Much success
          break;
        }

        // This might be too cute?
        if ((result.Status == HttpStatusCode.Unauthorized
            || result.Status == HttpStatusCode.NotFound)
          && request.CacheOptions != null
          && client.DefaultToken != request.CacheOptions?.AccessToken) {
          // Try again with default credentials
          // HACK: Better to clear expired/stale metadata, but that's hard.
          request.CacheOptions = null;
          continue;
        } else if (result.ErrorSeverity == GitHubErrorSeverity.RateLimited 
          && request.CacheOptions != null
          && client.DefaultToken != request.CacheOptions?.AccessToken) {
          // HACK: really need to load balance tokens instead
          request.CacheOptions = null;
          continue;
        } else if (result.ErrorSeverity != GitHubErrorSeverity.Retry) {
          // Unrecoverable, abort.
          break;
        }

        await Task.Delay(RetryMilliseconds * (i + 1));
      }

      return result;
    }

    [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "PaginationHandler")]
    [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "GitHubHandler")]
    public Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector) {
      throw new NotSupportedException($"{nameof(GitHubHandler)} only supports single fetches. Is {nameof(PaginationHandler)} missing from the pipeline?");
    }

    private async Task<GitHubResponse<T>> MakeRequest<T>(GitHubClient client, GitHubRequest request, GitHubRedirect redirect) {
      var uri = new Uri(client.ApiRoot, request.Uri);
      var httpRequest = new HttpRequestMessage(request.Method, uri) {
        Content = request.CreateBodyContent(),
      };

      // Accept
      if (!request.AcceptHeaderOverride.IsNullOrWhiteSpace()) {
        httpRequest.Headers.Accept.Clear();
        httpRequest.Headers.Accept.ParseAdd(request.AcceptHeaderOverride);
      }

      // Restricted
      if (request.Restricted && request.CacheOptions?.AccessToken != client.DefaultToken) {
        // When requesting restricted data, cached and current access tokens must match.
        request.CacheOptions = null;
      }

      // Authentication (prefer token from cache metadata if present)
      var accessToken = request.CacheOptions?.AccessToken ?? client.DefaultToken;
      httpRequest.Headers.Authorization = new AuthenticationHeaderValue("token", accessToken);

      // Caching (Only for GETs when not restricted or token matches)
      if (request.Method == HttpMethod.Get && request.CacheOptions != null) {
        var cache = request.CacheOptions;
        httpRequest.Headers.IfModifiedSince = cache.LastModified;
        if (!cache.ETag.IsNullOrWhiteSpace()) {
          httpRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cache.ETag));
        }
      }

      // User agent
      httpRequest.Headers.UserAgent.Clear();
      httpRequest.Headers.UserAgent.Add(client.UserAgent);

      // For logging
      LoggingMessageProcessingHandler.SetLogBlobName(
        httpRequest,
        $"{client.UserInfo}/{client.CorrelationId}/{client.NextRequestId()}_{DateTime.UtcNow:o}{httpRequest.RequestUri.PathAndQuery}.log"
      );

      var response = await HttpClient.SendAsync(httpRequest);

      // Handle redirects
      switch (response.StatusCode) {
        case HttpStatusCode.MovedPermanently:
        case HttpStatusCode.RedirectKeepVerb:
          request = request.CloneWithNewUri(response.Headers.Location, preserveCache: true);
          return await MakeRequest<T>(client, request, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        case HttpStatusCode.Redirect:
        case HttpStatusCode.RedirectMethod:
          request = request.CloneWithNewUri(response.Headers.Location, preserveCache: true);
          request.Method = HttpMethod.Get;
          return await MakeRequest<T>(client, request, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        default:
          break;
      }

      var result = new GitHubResponse<T>(request) {
        Date = response.Headers.Date.Value,
        IsError = !response.IsSuccessStatusCode,
        Redirect = redirect,
        Status = response.StatusCode,
      };

      // Cache Headers
      result.CacheData = new GitHubCacheDetails() {
        AccessToken = accessToken,
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
      result.CacheData.Expires = result.Date.Add(result.CacheData.PollInterval);

      // Rate Limits
      // These aren't always sent. Check for presence and fail gracefully.
      if (response.Headers.Contains("X-RateLimit-Limit")) {
        result.RateLimit = new GitHubRateLimit() {
          AccessToken = accessToken,
          RateLimit = response.ParseHeader("X-RateLimit-Limit", x => int.Parse(x)),
          RateLimitRemaining = response.ParseHeader("X-RateLimit-Remaining", x => int.Parse(x)),
          RateLimitReset = response.ParseHeader("X-RateLimit-Reset", x => EpochUtility.ToDateTimeOffset(int.Parse(x))),
        };
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
      var scopes = response.ParseHeader<IEnumerable<string>>("X-OAuth-Scopes", x => (x == null) ? null : x.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
      if (scopes != null) {
        result.Scopes.UnionWith(scopes);
      }

      // Pagination
      // Screw the RFC, minimally match what GitHub actually sends.
      result.Pagination = response.ParseHeader("Link", x => (x == null) ? null : GitHubPagination.FromLinkHeader(x));

      if (response.IsSuccessStatusCode) {
        // TODO: Handle accepted, etc.
        if (response.StatusCode == HttpStatusCode.NoContent && typeof(T) == typeof(bool)) {
          result.Result = (T)(object)true;
        } else if (response.StatusCode != HttpStatusCode.NotModified) {
          result.Result = await response.Content.ReadAsAsync<T>(GitHubSerialization.MediaTypeFormatters);
        }
      } else {
        if (response.Content != null) {
          result.Error = await response.Content.ReadAsAsync<GitHubError>(GitHubSerialization.MediaTypeFormatters);
        }
        switch (response.StatusCode) {
          case HttpStatusCode.BadRequest:
          case HttpStatusCode.Unauthorized:
            result.ErrorSeverity = GitHubErrorSeverity.Failed;
            break;
          case HttpStatusCode.Forbidden:
            if (result.RateLimit == null || result.Error.IsAbuse) {
              result.ErrorSeverity = GitHubErrorSeverity.Abuse;
            } else if (result.RateLimit.RateLimitRemaining == 0) {
              result.ErrorSeverity = GitHubErrorSeverity.RateLimited;
            }
            break;
          case HttpStatusCode.BadGateway:
          case HttpStatusCode.GatewayTimeout:
          case HttpStatusCode.InternalServerError:
          case HttpStatusCode.ServiceUnavailable:
            result.ErrorSeverity = GitHubErrorSeverity.Retry;
            break;
        }
      }

      return result;
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    private static HttpClient CreateGitHubHttpClient() {
      var useFiddler = false;
#if DEBUG
      if (!bool.TryParse(CloudConfigurationManager.GetSetting("UseFiddler") ?? "", out useFiddler)) {
        useFiddler = false;
      }
#endif

      HttpMessageHandler handler = new HttpClientHandler() {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
        AllowAutoRedirect = false,
        MaxRequestContentBufferSize = 4 * 1024 * 1024,
        UseCookies = false,
        UseDefaultCredentials = false,
#if DEBUG
        UseProxy = useFiddler,
        Proxy = useFiddler ? new WebProxy("127.0.0.1", 8888) : null,
#endif
      };

      // This is a gross hack
#if DEBUG
      if (useFiddler) {
        ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => { return true; };
      }
#endif

      // TODO: Inject this or something.
      var gitHubLoggingStorage = CloudConfigurationManager.GetSetting("GitHubLoggingStorage");
      if (!gitHubLoggingStorage.IsNullOrWhiteSpace()) {
        var account = CloudStorageAccount.Parse(gitHubLoggingStorage);
        var logHandler = new LoggingMessageProcessingHandler(account, "github-logs", handler);
        handler = logHandler;
      }

      var httpClient = new HttpClient(handler, true);

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
  }
}
