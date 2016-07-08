namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Net.Http;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;

  [RoutePrefix("github")]
  public class GitHubProxyController : ShipHubController {
    private static readonly HttpClient _ProxyClient = new HttpClient() {
      MaxResponseContentBufferSize = 1024 * 1024 * 5, // 5MB is pretty generous
      Timeout = TimeSpan.FromSeconds(10), // so is 10 seconds
    };

    private static readonly HashSet<HttpMethod> _BareMethods = new HashSet<HttpMethod>() { HttpMethod.Delete, HttpMethod.Get, HttpMethod.Head, HttpMethod.Options };

    [HttpGet]
    [HttpPut]
    [HttpHead]
    [HttpPost]
    [HttpDelete]
    [HttpOptions]
    [Route("{*path}")]
    public Task<HttpResponseMessage> ProxyBlind(HttpRequestMessage request, CancellationToken cancellationToken, string path) {
      var builder = new UriBuilder(request.RequestUri);
      builder.Scheme = Uri.UriSchemeHttps;
      builder.Port = 443;
      builder.Host = "api.github.com";
      builder.Path = path;
      request.RequestUri = builder.Uri;

      request.Headers.Host = request.RequestUri.Host;

      // This is dumb
      if (_BareMethods.Contains(request.Method)) {
        request.Content = null;
      }

      return _ProxyClient.SendAsync(request, cancellationToken);
    }
  }
}
