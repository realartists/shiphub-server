namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.IO;
  using System.IO.Compression;
  using System.Net;
  using System.Net.Http;
  using System.Net.WebSockets;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web;
  using System.Web.Http;
  using System.Web.WebSockets;
  using Common;
  using Common.WebSockets;

  [RoutePrefix("Sync")]
  public class SyncController : ShipHubController {
    [Route("")]
    [HttpGet]
    public HttpResponseMessage Sync() {
      var context = HttpContext.Current;
      if (context.IsWebSocketRequest) {
        var handler = new SyncConnection();
        context.AcceptWebSocketRequest(handler.AcceptWebSocketRequest, new AspNetWebSocketOptions() { SubProtocol = "V1" });
        return new HttpResponseMessage(HttpStatusCode.SwitchingProtocols);
      }

      return new HttpResponseMessage(HttpStatusCode.UpgradeRequired) {
        ReasonPhrase = "WebSocket connection required."
      };
    }
  }

  public class SyncConnection : WebSocketHandler {
    private const int _MaxMessageSize = 64 * 1024; // 64 KB

    public SyncConnection()
      : base(_MaxMessageSize) {
    }

    public override void OnClose() {
      // TODO: Remove from active connections
    }

    public override void OnError() {
      // OnClose is always called after OnError.
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public override void OnMessage(byte[] message) {
      var gzip = message[0] == 1;
      string json = "";
      if (gzip) {
        using (var ms = new MemoryStream(message))
        using (var df = new GZipStream(ms, CompressionMode.Decompress))
        using (var tr = new StreamReader(df, Encoding.UTF8)) {
          ms.ReadByte(); // eat gzip flag
          json = tr.ReadToEnd();
        }
      } else {
        json = Encoding.UTF8.GetString(message, 1, message.Length - 1);
      }

      OnMessage(json);
    }

    public override void OnMessage(string message) {
      // TODO: Dispatch
    }

    public Task AcceptWebSocketRequest(AspNetWebSocketContext context) {
      return ProcessWebSocketRequestAsync(context.WebSocket, CancellationToken.None);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public Task SendJsonAsync(object o) {
      using (var ms = new MemoryStream()) {
        ms.WriteByte(1);

        using (var df = new GZipStream(ms, CompressionLevel.Optimal))
        using (var sw = new StreamWriter(df, Encoding.UTF8)) {
          JsonUtility.SaneSerializer.Serialize(sw, o);
          sw.Flush();
        }

        return SendAsync(new ArraySegment<byte>(ms.ToArray()), WebSocketMessageType.Binary, true);
      }
    }
  }
}
