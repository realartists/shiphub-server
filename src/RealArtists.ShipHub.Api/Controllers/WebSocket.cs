namespace RealArtists.ShipHub.Api.Controllers {
  using System.Net.WebSockets;
  using System.Threading;
  using System.Threading.Tasks;
  using Microsoft.AspNetCore.Mvc;

  [Route("api/[controller]")]
  public class WebSocket : Controller {
    public const string ShipHubWebSocketProtocol = "ShipHub";

    public async Task<IActionResult> Index() {
      var ws = HttpContext.WebSockets;
      if (ws.IsWebSocketRequest && ws.WebSocketRequestedProtocols.Contains(ShipHubWebSocketProtocol)) {
        var socket = await ws.AcceptWebSocketAsync(ShipHubWebSocketProtocol);

        await socket.CloseAsync(WebSocketCloseStatus.Empty, "Because Reasons", CancellationToken.None);
      }

      return Ok();
    }
  }
}
