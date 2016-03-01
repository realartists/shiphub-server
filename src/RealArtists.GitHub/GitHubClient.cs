namespace RealArtists.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Net.Http.Headers;
  using System.Net.Mime;
  using System.Text;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;

  public class GitHubClient : IDisposable {
    private static readonly string _Version = typeof(GitHubClient).Assembly.GetName().Version.ToString();
    private static readonly Uri _ApiRoot = new Uri("https://api.github.com/");
    //private static readonly MediaTypeHeaderValue _JsonMediaTypeHeader = new MediaTypeHeaderValue("application/json");
    private static readonly JsonSerializerSettings _JsonSettings;
    private static readonly JsonMediaTypeFormatter _JsonMediaTypeFormatter;
    private static readonly MediaTypeFormatter[] _MediaTypeFormatters;

    static GitHubClient() {
      _JsonSettings = new JsonSerializerSettings() {
        ContractResolver = new SnakeCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
      };
      _JsonSettings.Converters.Add(new StringEnumConverter());
      _JsonMediaTypeFormatter = new JsonMediaTypeFormatter() {
        SerializerSettings = _JsonSettings,
      };
      _MediaTypeFormatters = new[] { _JsonMediaTypeFormatter };
    }

    private HttpClient _httpClient;

    public GitHubClient(IGitHubCredentials credentials) {
      var handler = new HttpClientHandler() {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
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

      credentials.Apply(headers);
    }

    protected static KeyValuePair<string, string> Pair(string key, object value) {
      return Pair(key, value.ToString());
    }

    protected static KeyValuePair<string, string> Pair(string key, DateTime value) {
      return Pair(key, value.ToUniversalTime().ToString("o"));
    }

    protected static KeyValuePair<string, string> Pair(string key, DateTimeOffset value) {
      return Pair(key, value.ToString("o"));
    }

    protected static KeyValuePair<string, string> Pair(string key, string value) {
      return new KeyValuePair<string, string>(key, value);
    }

    protected async Task<TResponse> MakeRequest<TRequest, TResponse>(HttpMethod method, string relativePath, TRequest body, params KeyValuePair<string, string>[] queryParams)
      where TResponse : GitHubResponse {
      Uri uri;
      if (queryParams.Any()) {
        var query = string.Join("&", queryParams.Select(x => $"{Uri.EscapeUriString(x.Key)}={Uri.EscapeUriString(x.Value)}"));
        uri = new Uri(_ApiRoot, string.Join("?", relativePath, query));
      } else {
        uri = new Uri(_ApiRoot, relativePath);
      }

      var request = new HttpRequestMessage(method, uri);

      if (body != null) {
        request.Content = new ObjectContent<TRequest>(body, _JsonMediaTypeFormatter);
      }

      GitHubResponse result;
      try {
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        result = await response.Content.ReadAsAsync<TResponse>(_MediaTypeFormatters);
      } catch (Exception e) {
        throw new SlackException("Exception during HTTP processing.", e);
      }

      return (TResponse)result;
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

