namespace WebSocketTestClient {
  using System;
  using System.Configuration;
  using System.Data.Entity;
  using System.IO;
  using System.IO.Compression;
  using System.Linq;
  using System.Net.WebSockets;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using Newtonsoft.Json.Linq;
  using RealArtists.ShipHub.Common;
  using RealArtists.ShipHub.Common.DataModel;

  static class Program {
    static void Main(string[] args) {
      try {
        //DoAsync().GetAwaiter().GetResult();
        //PingTest().GetAwaiter().GetResult();
      } catch (Exception e) {
        Console.WriteLine(e.ToString());
        Console.ReadKey();
      }
    }

    static async Task PingTest() {
      using (var context = new ShipHubContext()) {
        var user = await context.Users.SingleAsync(x => x.Login == "kogir");
        var ghc = GitHubSettings.CreateUserClient(user, Guid.NewGuid());
        var hooks = await ghc.RepositoryWebhooks("realartists/shiphub-server");
        var hook = hooks.Result.First();
        var ping = await ghc.PingRepositoryWebhook("realartists/shiphub-server", hook.Id);
      }
    }

    static async Task DoAsync() {
      using (var ws = new ClientWebSocket()) {
        var token = ConfigurationManager.AppSettings["GitHubTestToken"];
        var apiHost = ConfigurationManager.AppSettings["ApiHostName"];

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
         ""version"": 73779
      }
    ],
    ""orgs"": [
      {
        ""org"": 12961054,
        ""version"": 71312
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
