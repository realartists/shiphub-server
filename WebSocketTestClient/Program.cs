namespace WebSocketTestClient {
  using System;
  using System.Configuration;
  using System.IO;
  using System.IO.Compression;
  using System.Net.WebSockets;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using Newtonsoft.Json.Linq;
  using RealArtists.ShipHub.Common;

  static class Program {
    static void Main(string[] args) {
      try {
        DoAsync().Wait();
      } catch (Exception e) {
        Console.WriteLine(e.ToString());
        Console.ReadKey();
      }
    }

    static async Task DoAsync() {
      using (var ws = new ClientWebSocket()) {
        var token = ConfigurationManager.AppSettings["GitHubTestToken"];
        var apiHost = ConfigurationManager.AppSettings["ApiHostname"];

        ws.Options.AddSubProtocol("V1");
        ws.Options.SetRequestHeader("Authorization", $"token {token}");
        await ws.ConnectAsync(new Uri($"wss://{apiHost}/api/sync"), CancellationToken.None);
        if (ws.State != WebSocketState.Open) {
          Console.WriteLine("Unable to open socket.");
          return;
        }

        var versions = JToken.Parse(@"
{
    ""repos"": [
      {
          ""repo"": 51336290,
        ""version"": 128747
      },
      {
          ""repo"": 60724235,
        ""version"": 128170
      },
      {
          ""repo"": 37700616,
        ""version"": 128162
      },
      {
          ""repo"": 60808709,
        ""version"": 128022
      },
      {
          ""repo"": 47661297,
        ""version"": 128038
      },
      {
          ""repo"": 56088953,
        ""version"": 128002
      },
      {
          ""repo"": 46592358,
        ""version"": 128161
      },
      {
          ""repo"": 51336366,
        ""version"": 129400
      },
      {
          ""repo"": 53166137,
        ""version"": 128030
      },
      {
          ""repo"": 59613425,
        ""version"": 129161
      },
      {
          ""repo"": 5135507,
        ""version"": 142562
      },
      {
          ""repo"": 20194808,
        ""version"": 144301
      },
      {
          ""repo"": 25478804,
        ""version"": 141847
      },
      {
          ""repo"": 4342537,
        ""version"": 141846
      },
      {
          ""repo"": 38027930,
        ""version"": 144008
      },
      {
          ""repo"": 1234,
        ""version"": 144008
      }
    ],
    ""orgs"": [
      {
        ""org"": 7704921,
        ""version"": 134213
      },
      {
        ""org"": 1234,
        ""version"": 134213
      },
      {
        ""org"": 12961054,
        ""version"": 127962
      }
    ]
  }
");

        // Send Hello
        await SendMessage(ws, new {
          Msg = "hello",
          Client = "Nick's WebSocketTester",
          Versions = versions,
        });

        try {
          using (var sw = new StreamWriter("result.txt", false, Encoding.UTF8)) {
            // Read response
            while (ws.State == WebSocketState.Open) {
              var response = await ReadMessage(ws);
              //Console.WriteLine(response);
              Console.WriteLine("Message Received");
              sw.WriteLine(response);
              sw.Flush();
            }
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
