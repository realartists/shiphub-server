namespace RealArtists.ShipHub.Api.Controllers {
  using System.Diagnostics.CodeAnalysis;
  using System.Net;
  using System.Net.Http;
  using System.Net.Mime;
  using System.Text;
  using System.Web;
  using System.Web.Http;
  using System.Web.WebSockets;
  using Common.DataModel;
  using QueueClient;
  using Sync;

  [RoutePrefix("api/sync")]
  public class SyncController : ShipHubController {
    private ISyncManager _syncManager;
    private IShipHubQueueClient _queueClient;

    public SyncController(ShipHubContext context, ISyncManager syncManager, IShipHubQueueClient queueClient) : base(context) {
      _syncManager = syncManager;
      _queueClient = queueClient;
    }

    [Route("")]
    [HttpGet]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public HttpResponseMessage Sync() {
      var context = HttpContext.Current;
      if (context.IsWebSocketRequest) {
        var handler = new SyncConnection(ShipHubUser, _syncManager, _queueClient);
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
