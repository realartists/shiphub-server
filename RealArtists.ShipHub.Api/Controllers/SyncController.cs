namespace RealArtists.ShipHub.Api.Controllers {
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Mime;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web;
  using System.Web.Http;
  using System.Web.WebSockets;
  using Sync;

  [RoutePrefix("api/sync")]
  public class SyncController : ShipHubController {
    private static readonly SyncManager _SyncManager = new SyncManager();

    [Route("")]
    [HttpGet]
    public async Task<HttpResponseMessage> Sync() {
      var context = HttpContext.Current;
      if (context.IsWebSocketRequest) {
        var token = await Context.AccessTokens
          .Where(x => x.Account.Id == ShipUser.UserId)
          .Select(x => x.Token)
          .FirstAsync();
        var handler = new SyncConnection(ShipUser.UserId, token, _SyncManager);
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
