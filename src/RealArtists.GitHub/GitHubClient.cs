namespace RealArtists.GitHub {
  using System;
  using System.Globalization;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Net.Http.Headers;
  using System.Threading.Tasks;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;

  public class GitHubClient : IDisposable {
    private static readonly string _Version = typeof(GitHubClient).Assembly.GetName().Version.ToString();
    private static readonly Uri _ApiRoot = new Uri("https://api.github.com/");
    private static readonly JsonSerializerSettings _JsonSettings;
    private static readonly MediaTypeFormatter[] _MediaTypeFormatters;

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

    public GitHubClient(IGitHubCredentials credentials = null) {
      var handler = new HttpClientHandler() {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
        AllowAutoRedirect = false,
        MaxAutomaticRedirections = 5,
        MaxRequestContentBufferSize = 4 * 1024 * 1024,
        UseCookies = false,
      };
      _httpClient = new HttpClient(handler, true);

      var headers = _httpClient.DefaultRequestHeaders;
      headers.AcceptEncoding.Clear();
      headers.AcceptEncoding.ParseAdd("gzip");
      headers.AcceptEncoding.ParseAdd("deflate");

      headers.AcceptCharset.Clear();
      headers.AcceptCharset.ParseAdd("utf-8");

      headers.Accept.Clear();
      headers.Accept.ParseAdd("application/vnd.github.v3+json");

      headers.UserAgent.Clear();
      headers.UserAgent.Add(new ProductInfoHeaderValue("RealArtists.GitHub", _Version));

      headers.Add("Time-Zone", "Etc/UTC");

      if (credentials != null) {
        credentials.Apply(headers);
      }
    }

    public async Task<GitHubResponse<T>> MakeRequest<T>(GitHubRequest request, GitHubRedirect redirect = null) {
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
          return await MakeRequest<T>(request, new GitHubRedirect(response.StatusCode, response.Headers.Location));
        case HttpStatusCode.Redirect:
        case HttpStatusCode.RedirectMethod:
          request.Method = HttpMethod.Get;
          request.Uri = response.Headers.Location;
          return await MakeRequest<T>(request, new GitHubRedirect(response.StatusCode, response.Headers.Location));
        default:
          break;
      }

      var result = new GitHubResponse<T>() {
        Redirect = redirect,
        Status = response.StatusCode,
        ETag = response.Headers.ETag?.Tag,
        LastModified = response.Content?.Headers?.LastModified,
      };

      result.RateLimit = response.Headers
        .Where(x => x.Key.Equals("X-RateLimit-Limit", StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => x.Value)
        .Select(x => int.Parse(x))
        .Single();

      result.RateLimitRemaining = response.Headers
        .Where(x => x.Key.Equals("X-RateLimit-Remaining", StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => x.Value)
        .Select(x => int.Parse(x))
        .Single();

      result.RateLimitReset = response.Headers
        .Where(x => x.Key.Equals("X-RateLimit-Reset", StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => x.Value)
        .Select(x => EpochDateTimeConverter.EpochToDateTimeOffset(int.Parse(x)))
        .Single();

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

    #region IDisposable Support
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
    #endregion
  }
}

