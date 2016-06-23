namespace WebSocketTestClient {
  using System;
  using System.Configuration;
  using System.IO;
  using System.IO.Compression;
  using System.Net.WebSockets;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using RealArtists.ShipHub.Common;

  static class Program {
    static void Main(string[] args) {
      DoAsync().Wait();
    }

    static async Task DoAsync() {
      using (var ws = new ClientWebSocket()) {
        var token = ConfigurationManager.AppSettings["GitHubTestToken"];

        ws.Options.AddSubProtocol("V1");
        ws.Options.SetRequestHeader("Authorization", $"token {token}");
        await ws.ConnectAsync(new Uri("wss://hub-nick.realartists.com/api/sync"), CancellationToken.None);
        if (ws.State != WebSocketState.Open) {
          Console.WriteLine("Unable to open socket.");
          return;
        }

        // Send Hello
        await SendMessage(ws, new {
          Msg = "hello",
          Client = "Nick's WebSocketTester",
          Versions = new {
            Repos = new object[0],
            Orgs = new object[0],
          },
        });

        try {
          // Read response
          while (ws.State == WebSocketState.Open) {
            var response = await ReadMessage(ws);
            Console.WriteLine(response);
          }
        } catch { }
      }
    }

    static async Task SendMessage(ClientWebSocket socket, object message) {
      using (var ms = new MemoryStream()) {
        ms.WriteByte(1);

        using (var df = new GZipStream(ms, CompressionLevel.Optimal))
        using (var sw = new StreamWriter(df, Encoding.UTF8)) {
          JsonUtility.SaneSerializer.Serialize(sw, message);
          sw.Flush();
        }

        await socket.SendAsync(new ArraySegment<byte>(ms.ToArray()), WebSocketMessageType.Binary, true, CancellationToken.None);
      }
    }

    static async Task<string> ReadMessage(ClientWebSocket socket) {
      var buffer = new byte[4096];
      using (var msm = new MemoryStream()) {
        while (true) {
          var segment = new ArraySegment<byte>(buffer);
          var result = await socket.ReceiveAsync(segment, CancellationToken.None);

          switch (result.MessageType) {
            case WebSocketMessageType.Text:
              break;
            case WebSocketMessageType.Binary:
              msm.Write(segment.Array, segment.Offset, result.Count);
              break;
            case WebSocketMessageType.Close:
              return null;
          }

          if (result.EndOfMessage) {
            break;
          }
        }

        var message = msm.ToArray();
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

        return json;
      }
    }
  }
}
