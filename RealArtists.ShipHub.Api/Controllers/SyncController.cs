namespace RealArtists.ShipHub.Api.Controllers {
  using System.Net;
  using System.Net.Http;
  using System.Net.Mime;
  using System.Text;
  using System.Web;
  using System.Web.Http;
  using System.Web.WebSockets;
  using Sync;

  [RoutePrefix("api/sync")]
  public class SyncController : ShipHubController {
    private static readonly SyncManager _SyncManager = new SyncManager();

    [Route("")]
    [HttpGet]
    public HttpResponseMessage Sync() {
      var context = HttpContext.Current;
      if (context.IsWebSocketRequest) {
        var handler = new SyncConnection(ShipHubUser, _SyncManager);
        context.AcceptWebSocketRequest(handler.AcceptWebSocketRequest, new AspNetWebSocketOptions() { SubProtocol = "V1" });
        return new HttpResponseMessage(HttpStatusCode.SwitchingProtocols);
      }

      var reason = "WebSocket connection required.";
      return new HttpResponseMessage(HttpStatusCode.UpgradeRequired) {
        ReasonPhrase = reason,
        Content = new StringContent(reason, Encoding.UTF8, MediaTypeNames.Text.Plain),
      };
    }
  }
}
