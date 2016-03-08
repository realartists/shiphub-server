namespace RealArtists.GitHub {
  using System;
  using System.Collections;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Net.Http.Headers;
  using System.Threading.Tasks;
  using Models;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;
  using Serialization;

  public static class HeaderUtility {
    public static string SingleHeader(this HttpResponseMessage response, string headerName) {
      return response.Headers
        .Where(x => x.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => x.Value)
        .SingleOrDefault();
    }

    public static T ParseHeader<T>(this HttpResponseMessage response, string headerName, Func<string, T> selector) {
      return selector(response.SingleHeader(headerName));
    }
  }

  public class GitHubClient : IDisposable {
    static readonly Uri _ApiRoot = new Uri("https://api.github.com/");
    static readonly JsonSerializerSettings _JsonSettings;
    static readonly MediaTypeFormatter[] _MediaTypeFormatters;

    public static readonly MediaTypeHeaderValue JsonMediaType = new MediaTypeHeaderValue("application/json");
    public static readonly JsonMediaTypeFormatter JsonMediaTypeFormatter;

    static GitHubClient() {
      _JsonSettings = new JsonSerializerSettings() {
        ContractResolver = new SnakeCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
      };
      _JsonSettings.Converters.Add(new StringEnumConverter() {
        AllowIntegerValues = false,
        CamelCaseText = true,
      });
      JsonMediaTypeFormatter = new JsonMediaTypeFormatter() {
        SerializerSettings = _JsonSettings,
      };
      _MediaTypeFormatters = new[] { JsonMediaTypeFormatter };
    }

    private HttpClient _httpClient;

    public GitHubClient(string productName, string productVersion, IGitHubCredentials credentials = null) {
      var handler = new HttpClientHandler() {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
        AllowAutoRedirect = false,
        MaxAutomaticRedirections = 5,
        MaxRequestContentBufferSize = 4 * 1024 * 1024,
        UseCookies = false,
        UseDefaultCredentials = false,
      };
      _httpClient = new HttpClient(handler, true);

      var headers = _httpClient.DefaultRequestHeaders;
      headers.AcceptEncoding.Clear();
      headers.AcceptEncoding.ParseAdd("gzip");
      headers.AcceptEncoding.ParseAdd("deflate");

      headers.Accept.Clear();
      headers.Accept.ParseAdd("application/vnd.github.v3+json");

      headers.AcceptCharset.Clear();
      headers.AcceptCharset.ParseAdd("utf-8");

      headers.UserAgent.Clear();
      headers.UserAgent.Add(new ProductInfoHeaderValue(productName, productVersion));

      headers.Add("Time-Zone", "Etc/UTC");

      if (credentials != null) {
        credentials.Apply(headers);
      }
    }

    public async Task<GitHubResponse<AccessTokenModel>> CreateAccessToken(string clientId, string clientSecret, string code, string state) {
      var request = new GitHubRequest<object>(HttpMethod.Post, "", new {
        ClientId = clientId,
        ClientSecret = clientSecret,
        Code = code,
        State = state,
      });

      var httpRequest = new HttpRequestMessage(request.Method, _OauthTokenRedemption) {
        Content = request.CreateBodyContent(),
      };
      httpRequest.Headers.Accept.Clear();
      httpRequest.Headers.Accept.ParseAdd("application/json");

      var response = await _httpClient.SendAsync(httpRequest);

      var result = new GitHubResponse<AccessTokenModel>() {
        Status = response.StatusCode,
        ETag = response.Headers.ETag?.Tag,
        LastModified = response.Content?.Headers?.LastModified,
      };

      if (response.IsSuccessStatusCode) {
        // TODO: Handle accepted, no content, etc.
        if (response.StatusCode != HttpStatusCode.NotModified) {
          result.Result = await response.Content.ReadAsAsync<AccessTokenModel>(_MediaTypeFormatters);
        }
      } else {
        result.Error = await response.Content.ReadAsAsync<GitHubError>(_MediaTypeFormatters);
      }

      return result;
    }

    public async Task<GitHubResponse<UserModel>> AuthenticatedUser() {
      var request = new GitHubRequest(HttpMethod.Get, "user");

      return await MakeRequest<UserModel>(request);
    }

    public async Task<GitHubResponse<T>> MakeRequest<T>(GitHubRequest request, GitHubRedirect redirect = null) {
      // Always request the biggest page size
      if (request.Method == HttpMethod.Get
        && typeof(IEnumerable).IsAssignableFrom(typeof(T))) {
        request.AddParameter("per_page", 100);
      }

      var uri = new Uri(_ApiRoot, request.Uri);
      var httpRequest = new HttpRequestMessage(request.Method, uri) {
        Content = request.CreateBodyContent(),
      };

      // Conditional headers
      httpRequest.Headers.IfModifiedSince = request.LastModified;
      if (!string.IsNullOrWhiteSpace(request.ETag)) {
        httpRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(request.ETag));
      }

      var response = await _httpClient.SendAsync(httpRequest);

      // Handle redirects
      switch (response.StatusCode) {
        case HttpStatusCode.MovedPermanently:
        case HttpStatusCode.RedirectKeepVerb:
          request.Uri = response.Headers.Location;
          return await MakeRequest<T>(request, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        case HttpStatusCode.Redirect:
        case HttpStatusCode.RedirectMethod:
          request.Method = HttpMethod.Get;
          request.Uri = response.Headers.Location;
          return await MakeRequest<T>(request, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        default:
          break;
      }

      var result = new GitHubResponse<T>() {
        Redirect = redirect,
        Status = response.StatusCode,
        ETag = response.Headers.ETag?.Tag,
        LastModified = response.Content?.Headers?.LastModified,
      };

      // Expires and Caching Max-Age
      result.Expires = response.Content?.Headers?.Expires;
      var maxAgeSpan = response.Headers.CacheControl?.SharedMaxAge ?? response.Headers.CacheControl?.MaxAge;
      if (maxAgeSpan != null) {
        var maxAgeExpires = DateTimeOffset.UtcNow.Add(maxAgeSpan.Value);
        if (result.Expires == null || maxAgeExpires < result.Expires) {
          result.Expires = maxAgeExpires;
        }
      }

      // Rate Limits
      result.RateLimit = response.ParseHeader("X-RateLimit-Limit", x => int.Parse(x));
      result.RateLimitRemaining = response.ParseHeader("X-RateLimit-Remaining", x => int.Parse(x));
      result.RateLimitReset = response.ParseHeader("X-RateLimit-Reset", x => EpochUtility.ToDateTimeOffset(int.Parse(x)));

      // Pagination
      // Screw the RFC, minimally match what GitHub actually sends.
      var linkHeader = response.SingleHeader("Link");
      if (linkHeader != null) {
        result.Pagination = GitHubPagination.FromLinkHeader(linkHeader);
      }

      if (response.IsSuccessStatusCode) {
        // TODO: Handle accepted, no content, etc.
        if (response.StatusCode != HttpStatusCode.NotModified) {
          result.Result = await response.Content.ReadAsAsync<T>(_MediaTypeFormatters);
        }
      } else {
        result.Error = await response.Content.ReadAsAsync<GitHubError>(_MediaTypeFormatters);
      }

      return result;
    }

    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing) {
      if (!disposedValue) {
        if (disposing) {
          _httpClient.Dispose();
          _httpClient = null;
        }

        disposedValue = true;
      }
    }
    public void Dispose() {
      Dispose(true);
    }
  }
}

