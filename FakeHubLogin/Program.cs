namespace FakeHubLogin {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.IO.Compression;
  using System.Net;
  using System.Net.Http;
  using System.Net.WebSockets;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using Newtonsoft.Json.Linq;
  using RealArtists.ShipHub.Common;

  public static class Program {
    public const string ApiRoot = "hub-nick.realartists.com";
    public const string ClientName = "FakeHubLogin/1.0";
    public const int MaxConcurrent = 50;

    private static HttpClient _client;

    static void Main(string[] args) {
      var handler = new WinHttpHandler() {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
        AutomaticRedirection = false,
        CookieUsePolicy = CookieUsePolicy.IgnoreCookies,
        WindowsProxyUsePolicy = WindowsProxyUsePolicy.DoNotUseProxy,
      };

      if (ShipHubCloudConfiguration.Instance.UseFiddler) {
        handler.WindowsProxyUsePolicy = WindowsProxyUsePolicy.UseCustomProxy;
        handler.Proxy = new WebProxy("127.0.0.1", 8888);
        handler.ServerCertificateValidationCallback = (request, cert, chain, sslPolicyErrors) => { return true; };
      }

      _client = new HttpClient(handler, true) {
        BaseAddress = new Uri($"https://{ApiRoot}/"),
      };

        var tokens = ParseTokens("Data\\fakehub_huge-users.txt");
      var batch = new Semaphore(MaxConcurrent, MaxConcurrent);
      foreach (var token in tokens) {
        batch.WaitOne();
        Task.Run(() => LoginUser(token, batch));
      }
      Console.WriteLine("Press enter to exit.");
      Console.ReadLine();
    }

    private static async Task LoginUser(string token, Semaphore sema) {
      try {
        var result = await _client.PostAsJsonAsync("api/authentication/login", new {
          accessToken = token,
          clientName = ClientName,
        });

        if (!result.IsSuccessStatusCode && result.Content != null) {
          var stringContent = await result.Content.ReadAsStringAsync();
          await Console.Error.WriteLineAsync(stringContent);
        }
      } catch (Exception e) {
        Log.Exception(e);
        await Console.Error.WriteLineAsync(e.ToString());
      } finally {
        sema.Release();
      }

      //await Sync(token);
    }

    private static IEnumerable<string> ParseTokens(string file) {
      var tokens = new HashSet<string>();
      var fileInfo = new FileInfo(file);
      if (fileInfo.Exists) {
        Console.Write($"Loading {fileInfo.FullName}...");
        var lines = File.ReadAllLines(fileInfo.FullName, Encoding.UTF8).ToHashSet();
        Console.WriteLine($"Done. Read {lines.Count} tokens.");
        tokens.UnionWith(lines);
      }
      return tokens;
    }

    static async Task Sync(string token) {
      using (var ws = new ClientWebSocket()) {
        ws.Options.AddSubProtocol("V1");
        ws.Options.SetRequestHeader("Authorization", $"token {token}");
        try {
          var uri = new Uri($"wss://{ApiRoot}/api/sync");
          await ws.ConnectAsync(uri, CancellationToken.None);
          if (ws.State != WebSocketState.Open) {
            await Console.Error.WriteLineAsync($"Unable to open socket for {token} to {uri}.");
            return;
          }

          var versions = JToken.Parse(@"{ ""repos"": [], ""orgs"": [] }");

          // Send Hello
          await SendMessage(ws, new {
            Msg = "hello",
            Client = ClientName,
            Versions = versions,
          });


          using (var sw = new StreamWriter($"{token}_sync.log", false, Encoding.UTF8)) {
            // Read response
            while (ws.State == WebSocketState.Open) {
              var response = await ReadMessage(ws);
              sw.WriteLine(response);
              sw.Flush();
            }
          }
        } catch (Exception e) {
          Log.Exception(e);
          await Console.Error.WriteLineAsync(e.ToString());
        }
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
        var json = "";
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
