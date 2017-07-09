namespace RealArtists.ShipHub.Common {
  using System.Net.Http;
  using System.Threading;
  using System.Threading.Tasks;

  public class StatisticsLoggingMessageHandler : DelegatingHandler {
    public StatisticsLoggingMessageHandler(HttpMessageHandler innerHandler)
      : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
      var result = await base.SendAsync(request, cancellationToken);

      StatHat.Count("HTTP");
      StatHat.Count($"HTTP.{(int)(result.StatusCode)}");
      StatHat.Count($"HTTP.{request.RequestUri.Host}");
      StatHat.Count($"HTTP.{request.RequestUri.Host}.{(int)(result.StatusCode)}");

      return result;
    }
  }
}
