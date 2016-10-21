﻿namespace RealArtists.ShipHub.Api.Controllers {
  using System.Diagnostics.CodeAnalysis;
  using System.Net;
  using System.Net.Http;
  using System.Net.Mime;
  using System.Text;
  using System.Web;
  using System.Web.Http;
  using System.Web.WebSockets;
  using Filters;
  using Orleans;
  using QueueClient;
  using Sync;

  [RoutePrefix("api/sync")]
  public class SyncController : ApiController {
    private ISyncManager _syncManager;
    private IShipHubQueueClient _queueClient;
    private IGrainFactory _grainFactory;

    public SyncController(ISyncManager syncManager, IShipHubQueueClient queueClient, IGrainFactory grainFactory) {
      _syncManager = syncManager;
      _queueClient = queueClient;
      _grainFactory = grainFactory;
    }

    [Route("")]
    [HttpGet]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public HttpResponseMessage Sync() {
      var context = HttpContext.Current;
      if (context.IsWebSocketRequest) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        var handler = new SyncConnection(user, _syncManager, _queueClient, _grainFactory);
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
