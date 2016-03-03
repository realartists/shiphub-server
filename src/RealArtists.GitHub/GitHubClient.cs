namespace RealArtists.GitHub {
  using System;
  using System.Collections;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Net.Http.Headers;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;

  public class GitHubClient : IDisposable {
    static readonly string _Version = typeof(GitHubClient).Assembly.GetName().Version.ToString();
    static readonly Uri _ApiRoot = new Uri("https://api.github.com/");
    static readonly JsonSerializerSettings _JsonSettings;
    static readonly MediaTypeFormatter[] _MediaTypeFormatters;

    // Link: <https://api.github.com/repositories/51336290/issues/events?page_size=5&page=2>; rel="next", <https://api.github.com/repositories/51336290/issues/events?page_size=5&page=3>; rel="last"
    const RegexOptions _RegexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase;
    static readonly Regex _LinkRegex = new Regex(@"<(?<link>[^>]+)>; rel=""(?<rel>first|last|next|prev){1}""(, )?", _RegexOptions);

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

      // Rate Limits
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

      // Pagination
      // Screw the RFC, minimally match what GitHub actually sends.
      if (response.Headers.Contains("Link")) {
        var linkHeader = response.Headers
          .Where(x => x.Key.Equals("Link", StringComparison.OrdinalIgnoreCase))
          .SelectMany(x => x.Value)
          .Single();

        var links = result.Pagination = new GitHubPagination();
        foreach (Match match in _LinkRegex.Matches(linkHeader)) {
          var linkUri = new Uri(match.Groups["link"].Value);
          switch (match.Groups["rel"].Value) {
            case "first":
              links.First = linkUri;
              break;
            case "last":
              links.Last = linkUri;
              break;
            case "next":
              links.Next = linkUri;
              break;
            case "prev":
              links.Previous = linkUri;
              break;
            default:  // Skip unknown values
              break;
          }
        }
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

