namespace RealArtists.ShipHub.Common {
  using System.Net.Http;
  using System.Threading;
  using System.Threading.Tasks;

  public class StatisticsLoggingMessageHandler : DelegatingHandler {
    public StatisticsLoggingMessageHandler(HttpMessageHandler innerHandler)
      : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
      var result = await base.SendAsync(request, cancellationToken);

      var host = request.RequestUri.Host;
      var status = (int)result.StatusCode;
      StatHat.Count($"http~total,{status},{host} total,{host} {status}");

      return result;
    }
  }
}
