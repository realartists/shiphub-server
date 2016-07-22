namespace RealArtists.ShipHub.Api.Sync {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.IO;
  using System.IO.Compression;
  using System.Linq;
  using System.Net.WebSockets;
  using System.Reactive;
  using System.Reactive.Concurrency;
  using System.Reactive.Linq;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.WebSockets;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.WebSockets;
  using Filters;
  using Messages;
  using Messages.Entries;
  using Newtonsoft.Json.Linq;
  using QueueClient;
  using se = Messages.Entries;

  public class SyncConnection : WebSocketHandler {
    private const int _MaxMessageSize = 64 * 1024; // 64 KB
    private static readonly ShipHubBusClient _QueueClient = new ShipHubBusClient();

    private static readonly IObservable<long> _PollInterval =
      Observable.Interval(TimeSpan.FromMinutes(2))
      .Publish()
      .RefCount();


    private ShipHubPrincipal _user;
    private SyncManager _syncManager;
    private SyncContext _syncContext;

    private IDisposable _syncSubscription;
    private IDisposable _pollSubscription;

    public SyncConnection(ShipHubPrincipal user, SyncManager syncManager)
      : base(_MaxMessageSize) {
      _user = user;
      _syncManager = syncManager;
    }

    public override Task OnClose() {
      Unsubscribe();
      return Task.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public override Task OnMessage(byte[] message) {
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

      return OnMessage(json);
    }

    public override async Task OnMessage(string message) {
      var jobj = JObject.Parse(message);
      var data = jobj.ToObject<SyncRequestBase>(JsonUtility.SaneSerializer);
      switch (data.MessageType) {
        case "hello":
          // parse message, update local versions
          var hello = jobj.ToObject<HelloRequest>(JsonUtility.SaneSerializer);
          _syncContext = new SyncContext(_user, this, new SyncVersions(
            hello.Versions?.Repositories?.ToDictionary(x => x.Id, x => x.Version),
            hello.Versions?.Organizations?.ToDictionary(x => x.Id, x => x.Version)));
          Subscribe(); // Also performs the initial sync
          return;
        case "viewing":
          var viewing = jobj.ToObject<ViewingRequest>(JsonUtility.SaneSerializer);
          var parts = viewing.Issue.Split('#');
          var repoFullName = parts[0];
          var issueNumber = int.Parse(parts[1]);
          var qc = new ShipHubBusClient();
          await qc.SyncRepositoryIssueTimeline(_accessToken, repoFullName, issueNumber);
          return;
        default:
          // Ignore unknown messages for now
          return;
      }
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

    private void Subscribe() {
      if (_syncSubscription != null) {
        throw new InvalidOperationException("Already subscribed to changes.");
      }

      // Changes streamed from the queue
      _syncSubscription = _syncManager.Changes
        .ObserveOn(TaskPoolScheduler.Default)
        .Where(c => _syncContext.ShouldSync(c))
        .Select(x => Unit.Default)
        .StartWith(Unit.Default) // Run an initial sync no matter what.
        .Select(_ =>
          Observable.FromAsync(_syncContext.Sync)
          .Catch<Unit, Exception>(LogError<Unit>))
        .Concat() // Force sequentual evaluation
        .Subscribe();

      // Polling for updates
      _pollSubscription = _PollInterval
        .ObserveOn(TaskPoolScheduler.Default)
        .Select(_ =>
          Observable.FromAsync(() => _QueueClient.SyncAccount(_user.UserId))
          .Catch<Unit, Exception>(LogError<Unit>))
        .Concat() // Force sequentual evaluation
        .Subscribe();
    }

    private IObservable<T> LogError<T>(Exception exception) {
#if DEBUG
      Debugger.Break();
#endif
      return Observable.Empty<T>();
    }

    private void Unsubscribe() {
      if (_syncSubscription != null) {
        _syncSubscription.Dispose();
        _syncSubscription = null;
      }

      if (_pollSubscription != null) {
        _pollSubscription.Dispose();
        _pollSubscription = null;
      }
    }
  }
}
