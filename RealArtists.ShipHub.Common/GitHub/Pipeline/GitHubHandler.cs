namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Threading;
  using System.Threading.Tasks;

  public interface IGitHubHandler {
    Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request);
    Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector);
  }

  /// <summary>
  /// This is the core GitHub handler. It's the end of the line and does not delegate.
  /// Currently supports redirects, pagination, rate limits and limited retry logic.
  /// </summary>
  public class GitHubHandler : IGitHubHandler {
#if DEBUG
    public const bool UseFiddler = true;
#endif
    public const int PerFetchConcurrencyLimit = 16;
    public const int MaxRetries = 2;
    public const int PageSize = 100;

    public static HttpClient HttpClient { get; } = CreateGitHubHttpClient();
    public static int RetryMilliseconds { get; } = 1000;

    public uint RateLimitFloor { get; set; }

    public GitHubHandler() : this(500) { }
    public GitHubHandler(uint rateLimitFloor) {
      RateLimitFloor = rateLimitFloor;
    }

    public async Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request) {
      client.RateLimit.ThrowIfUnder(RateLimitFloor);

      GitHubResponse<T> result = null;

      for (int i = 0; i <= MaxRetries; ++i) {
        result = await MakeRequest<T>(client, request, null);

        if (result.RateLimit != null) {
          client.UpdateInternalRateLimit(result.RateLimit);
        }

        if (!result.IsError || result.ErrorSeverity != GitHubErrorSeverity.Retry) {
          break;
        }

        await Task.Delay(RetryMilliseconds * (i + 1));
      }

      return result;
    }

    public async Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector) {
      if (request.Method != HttpMethod.Get) {
        throw new InvalidOperationException("Only GETs are supported for pagination.");
      }

      // Always request the largest page size
      if (!request.Parameters.ContainsKey("per_page")) {
        request.AddParameter("per_page", PageSize);
      }

      // Fetch has the retry logic.
      var result = await Fetch<IEnumerable<T>>(client, request);

      if (!result.IsError && result.Pagination != null) {
        result = await EnumerateParallel<IEnumerable<T>, T>(client, result);
      }

      return result.Distinct(keySelector);
    }

    private async Task<GitHubResponse<T>> MakeRequest<T>(GitHubClient client, GitHubRequest request, GitHubRedirect redirect) {
      var uri = new Uri(client.ApiRoot, request.Uri);
      var httpRequest = new HttpRequestMessage(request.Method, uri) {
        Content = request.CreateBodyContent(),
      };

      // Accept
      if (!string.IsNullOrWhiteSpace(request.AcceptHeaderOverride)) {
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
        if (!string.IsNullOrWhiteSpace(cache.ETag)) {
          httpRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cache.ETag));
        }
      }

      // User agent
      httpRequest.Headers.UserAgent.Clear();
      httpRequest.Headers.UserAgent.Add(client.UserAgent);

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

    private async Task<GitHubResponse<TCollection>> EnumerateParallel<TCollection, TItem>(GitHubClient client, GitHubResponse<TCollection> firstPage)
      where TCollection : IEnumerable<TItem> {
      var results = new List<TItem>(firstPage.Result);
      IEnumerable<GitHubResponse<TCollection>> batch;

      // TODO: Cancellation (for when errors are encountered)?

      if (firstPage.Pagination?.CanInterpolate == true) {
        var pages = firstPage.Pagination.Interpolate();
        var pageRequestors = pages
          .Select(page => {
            Func<Task<GitHubResponse<TCollection>>> requestor = () => {
              var request = firstPage.Request.CloneWithNewUri(page);
              return Fetch<TCollection>(client, request);
            };

            return requestor;
          }).ToArray();


        // Check if we can request all the pages within the limit.
        if (firstPage.RateLimit.RateLimitRemaining < (pageRequestors.Length + RateLimitFloor)) {
          firstPage.Result = default(TCollection);
          firstPage.IsError = true;
          firstPage.ErrorSeverity = GitHubErrorSeverity.RateLimited;
          return firstPage;
        }

        batch = await Batch(pageRequestors);

        foreach (var response in batch) {
          if (response.IsError) {
            return response;
          } else {
            results.AddRange(response.Result);
          }
        }
      } else { // Walk in order
        var current = firstPage;
        while (current.Pagination?.Next != null) {
          var nextReq = current.Request.CloneWithNewUri(current.Pagination.Next);
          current = await Fetch<TCollection>(client, nextReq);

          if (current.IsError) {
            return current;
          } else {
            results.AddRange(current.Result);
          }
        }
        // Just use the last request.
        batch = new[] { current };
      }

      // Keep cache and other headers from first page.
      var final = firstPage;
      final.Result = (TCollection)(IEnumerable<TItem>)results;

      var rateLimit = final.RateLimit;
      foreach (var req in batch) {
        // Rate Limit
        var limit = req.RateLimit;
        if (limit.RateLimitReset > rateLimit.RateLimitReset) {
          rateLimit = limit;
        } else if (limit.RateLimitReset == rateLimit.RateLimitReset) {
          rateLimit.RateLimit = Math.Min(rateLimit.RateLimit, limit.RateLimit);
          rateLimit.RateLimitRemaining = Math.Min(rateLimit.RateLimitRemaining, limit.RateLimitRemaining);
        } // else ignore it
      }

      return final;
    }

    public async Task<IEnumerable<T>> Batch<T>(IEnumerable<Func<Task<T>>> batchTasks) {
      var tasks = new List<Task<T>>();
      using (var limit = new SemaphoreSlim(PerFetchConcurrencyLimit, PerFetchConcurrencyLimit)) {
        foreach (var item in batchTasks) {
          await limit.WaitAsync();

          tasks.Add(Task.Run(async delegate {
            var response = await item();
            limit.Release();
            return response;
          }));
        }

        await Task.WhenAll(tasks);

        var results = new List<T>();
        foreach (var task in tasks) {
          // we know they've completed.
          results.Add(task.Result);
        }

        return results;
      }
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    private static HttpClient CreateGitHubHttpClient() {
      var handler = new HttpClientHandler() {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
        AllowAutoRedirect = false,
        MaxRequestContentBufferSize = 4 * 1024 * 1024,
        UseCookies = false,
        UseDefaultCredentials = false,
#if DEBUG
        UseProxy = UseFiddler,
        Proxy = UseFiddler ? new WebProxy("127.0.0.1", 8888) : null,
#endif
      };

      // This is a gross hack
#if DEBUG
      if (UseFiddler) {
        ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => { return true; };
      }
#endif

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
