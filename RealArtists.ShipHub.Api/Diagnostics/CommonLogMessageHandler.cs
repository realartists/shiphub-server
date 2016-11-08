using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using RealArtists.ShipHub.Api.Filters;

namespace RealArtists.ShipHub.Api.Diagnostics {
  public class CommonLogMessageHandler : DelegatingHandler {
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) {
      var user = HttpContext.Current?.User as ShipHubPrincipal;
      if (user != null) {
        Common.Log.Info($"DebugIdentifier={user.DebugIdentifier} RequestUri={request.RequestUri}");
      }

      return base.SendAsync(request, cancellationToken);
    }
  }
}
