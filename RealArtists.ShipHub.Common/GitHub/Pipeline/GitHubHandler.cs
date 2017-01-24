namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Security.Authentication;
  using System.Threading;
  using System.Threading.Tasks;
  using Logging;
  using Microsoft.Azure;
  using Microsoft.WindowsAzure.Storage;

  public interface IGitHubHandler {
    Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request);
    Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector, ushort? maxPages = null);
  }

  /// <summary>
  /// This is the core GitHub handler. It's the end of the line and does not delegate.
  /// Currently supports redirects, pagination, rate limits and limited retry logic.
  /// </summary>
  public class GitHubHandler : IGitHubHandler {
    public const int LastAttempt = 2; // Make three attempts
    public const int RetryMilliseconds = 1000;

    // Should be less than Orleans timeout.
    // If changing, may also need to update values in CreateGitHubHttpClient()
    private static readonly TimeSpan _GitHubRequestTimeout = TimeSpan.FromSeconds(20);

    private static readonly HttpClient _HttpClient = CreateGitHubHttpClient();

    public async Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request) {
      if (client.RateLimit?.IsExceeded == true) {
        throw new GitHubRateException(client.UserInfo, request.Uri, client.RateLimit);
      }

      GitHubResponse<T> result = null;

      for (int attempt = 0; attempt <= LastAttempt; ++attempt) {
        if (attempt > 0) {
          await Task.Delay(RetryMilliseconds * attempt);
        }

        try {
          result = await MakeRequest<T>(client, request, null);
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

      if (result.RateLimit != null) {
        client.UpdateInternalRateLimit(result.RateLimit);
      }

      return result;
    }

    public Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector, ushort? maxPages = null) {
      throw new NotSupportedException($"{nameof(GitHubHandler)} only supports single fetches. Is {nameof(PaginationHandler)} missing from the pipeline?");
    }

    private async Task<GitHubResponse<T>> MakeRequest<T>(GitHubClient client, GitHubRequest request, GitHubRedirect redirect) {
      var uri = new Uri(client.ApiRoot, request.Uri);
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

      httpRequest.Headers.Authorization = new AuthenticationHeaderValue("token", client.AccessToken);

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
      LoggingMessageProcessingHandler.SetLogBlobName(
        httpRequest,
        $"{client.UserInfo}/{client.CorrelationId}/{client.NextRequestId()}_{DateTime.UtcNow:o}{httpRequest.RequestUri.PathAndQuery}.log"
      );

      HttpResponseMessage response;
      using (var timeout = new CancellationTokenSource(_GitHubRequestTimeout)) {
        try {
          response = await _HttpClient.SendAsync(httpRequest, timeout.Token);
        } catch (TaskCanceledException exception) {
          exception.Report($"GitHub timeout for {request.Uri} {LoggingMessageProcessingHandler.ExtractBlobName(httpRequest)}");
          throw;
        }
      }

      // Handle redirects
      switch (response.StatusCode) {
        case HttpStatusCode.MovedPermanently:
        case HttpStatusCode.RedirectKeepVerb:
          request = request.CloneWithNewUri(response.Headers.Location);
          return await MakeRequest<T>(client, request, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        case HttpStatusCode.Redirect:
        case HttpStatusCode.RedirectMethod:
          request = request.CloneWithNewUri(response.Headers.Location);
          request.Method = HttpMethod.Get;
          return await MakeRequest<T>(client, request, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
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
        AccessToken = client.AccessToken,
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
        } else if (response.Content != null) {
          // JSON formatted result
          result.Result = await response.Content.ReadAsAsync<T>(GitHubSerialization.MediaTypeFormatters);
        }
      } else if (response.Content != null) {
        var mediaType = response.Content.Headers.ContentType.MediaType;
        if (mediaType.Contains("github") || mediaType.Contains("json")) {
          result.Error = await response.Content.ReadAsAsync<GitHubError>(GitHubSerialization.MediaTypeFormatters);
        } else {
          var body = await response.Content.ReadAsStringAsync();
          throw new GitHubException($"Invalid GitHub Response:\n\n{body}");
        }
      }

      return result;
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    private static HttpClient CreateGitHubHttpClient() {
      var rootHandler = new WinHttpHandler() {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
        AutomaticRedirection = false,
        CheckCertificateRevocationList = true,
        CookieUsePolicy = CookieUsePolicy.IgnoreCookies,
        SslProtocols = SslProtocols.Tls12,
        WindowsProxyUsePolicy = WindowsProxyUsePolicy.DoNotUseProxy,
      };

#if DEBUG
      if (ShipHubCloudConfiguration.Instance.UseFiddler) {
        rootHandler.WindowsProxyUsePolicy = WindowsProxyUsePolicy.UseCustomProxy;
        rootHandler.Proxy = new WebProxy("127.0.0.1", 8888);
        rootHandler.ServerCertificateValidationCallback = (request, cert, chain, sslPolicyErrors) => { return true; };
      }
#endif

      HttpMessageHandler handler = rootHandler;

      // TODO: Inject this or something.
      // Always enable even if not storing bodies for generic request logging.
      var gitHubLoggingStorage = ShipHubCloudConfiguration.Instance.GitHubLoggingStorage;
      var logHandler = new LoggingMessageProcessingHandler(
        gitHubLoggingStorage.IsNullOrWhiteSpace() ? null : CloudStorageAccount.Parse(gitHubLoggingStorage),
        "github-logs",
        handler
      );
      handler = logHandler;

      var httpClient = new HttpClient(handler, true) {
        Timeout = _GitHubRequestTimeout,
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
  }
}
