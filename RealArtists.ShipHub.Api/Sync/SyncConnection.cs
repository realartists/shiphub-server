namespace RealArtists.ShipHub.Api.Sync {
  using System;
  using System.Diagnostics.CodeAnalysis;
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
  using ActorInterfaces;
  using Common;
  using Common.DataModel.Types;
  using Common.WebSockets;
  using Filters;
  using Messages;
  using Newtonsoft.Json.Linq;

  public interface ISyncConnection {
    Task SendJsonAsync(object message);
    Task CloseAsync();
    Version ClientBuild { get; }
  }

  public class SyncConnection : WebSocketHandler, ISyncConnection {
    private const int _MaxMessageSize = 1024 * 1024; // 1 MB
    private static readonly Version _MinimumClientBuild = new Version(338, 0);

    private static readonly IObservable<long> _PollInterval =
      Observable.Interval(TimeSpan.FromMinutes(2))
      .Publish()
      .RefCount();

    private ShipHubPrincipal _user;
    private SyncContext _syncContext;
    private ISyncManager _syncManager;
    private IAsyncGrainFactory _grainFactory;

    private IDisposable _syncSubscription;
    private IDisposable _pollSubscription;

    public Version ClientBuild { get; set; }

    public SyncConnection(ShipHubPrincipal user, ISyncManager syncManager, IAsyncGrainFactory grainFactory)
      : base(_MaxMessageSize) {
      _user = user;
      _syncManager = syncManager;
      _grainFactory = grainFactory;
    }

    public override Task OnError(Exception exception) {
      try {
        Unsubscribe();
      } finally {
        // Ensure we log the original error
        exception.Report("Socket error.", _user.DebugIdentifier);
      }

      return Task.CompletedTask;
    }

    public override Task OnClose() {
      Unsubscribe();
      return Task.CompletedTask;
    }

    [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public override Task OnMessage(byte[] message) {
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

      return OnMessage(json);
    }

    public override async Task OnMessage(string message) {
      Log.Debug(() => $"{_user.Login} {message}");
      var jobj = JObject.Parse(message);
      var data = jobj.ToObject<SyncMessageBase>(JsonUtility.JsonSerializer);
      switch (data.MessageType) {
        case "hello":
          if (_syncContext != null) {
            throw new InvalidOperationException("hello can only be sent once at initial connection of websocket.");
          }

          // parse message, update local versions
          var hello = jobj.ToObject<HelloRequest>(JsonUtility.JsonSerializer);

          // Validate version
          ClientBuild = hello.BuildVersion;
          if (ClientBuild == null || ClientBuild < _MinimumClientBuild) {
            await SendJsonAsync(new HelloResponse() {
              Upgrade = new UpgradeDetails() {
                Required = true
              }
            });
            return;
          }

          // now start sync
          _syncContext = new SyncContext(_user, this, new SyncVersions(
            hello.Versions?.Repositories?.ToDictionary(x => x.Id, x => x.Version),
            hello.Versions?.Organizations?.ToDictionary(x => x.Id, x => x.Version),
            hello.Versions?.PullRequestVersion,
            hello.Versions?.MentionsVersion)
          );
          await _syncContext.SendHelloResponse(Constants.PurgeIdentifier);

          var userActor = await _grainFactory.GetGrain<IUserActor>(_user.UserId);
          await userActor.SyncBillingState();

          Subscribe(userActor); // Also performs the initial sync
          return;
        case "viewing":
          var viewing = jobj.ToObject<ViewingRequest>(JsonUtility.JsonSerializer);
          var parts = viewing.Issue.Split('#');
          var repoFullName = parts[0];
          var issueNumber = int.Parse(parts[1]);
          var issueGrain = await _grainFactory.GetGrain<IIssueActor>(issueNumber, repoFullName, grainClassNamePrefix: null);
          issueGrain.SyncTimeline(_user.UserId, Common.GitHub.RequestPriority.Interactive).LogFailure(_user.DebugIdentifier);
          return;
        default:
          // Ignore unknown messages for now
          return;
      }
    }

    public Task AcceptWebSocketRequest(WebSocketContext context) {
      return ProcessWebSocketRequestAsync(context.WebSocket, CancellationToken.None);
    }

    [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
    public Task SendJsonAsync(object message) {
      using (var ms = new MemoryStream()) {
        ms.WriteByte(1);

        using (var df = new GZipStream(ms, CompressionLevel.Optimal))
        using (var sw = new StreamWriter(df, Encoding.UTF8)) {
          JsonUtility.JsonSerializer.Serialize(sw, message);
          sw.Flush();
        }

        try {
          return SendAsync(new ArraySegment<byte>(ms.ToArray()), WebSocketMessageType.Binary, true);
        } catch (ObjectDisposedException) {
          // Connection is dead.
          return OnClose();
        }
      }
    }

    private void Subscribe(IUserActor userActor) {
      Log.Info($"{_user.Login}");

      if (_syncSubscription != null) {
        throw new InvalidOperationException("Already subscribed to changes.");
      }

      var start = new ChangeSummary();
      start.Add(userId: _user.UserId);

      // Changes streamed from the queue
      _syncSubscription = _syncManager.Changes
        .ObserveOn(TaskPoolScheduler.Default)
        .StartWith(start) // Run an initial sync no matter what.
        .Select(c =>
          Observable.FromAsync(() => _syncContext.Sync(c))
          .Catch<Unit, Exception>(LogError<Unit>))
        .Concat() // Force sequential evaluation
        .Subscribe();

      // Polling for updates
      _pollSubscription = _PollInterval
        .ObserveOn(TaskPoolScheduler.Default)
        .StartWith(0)
        .Select(_ =>
          Observable.FromAsync(() => userActor.Sync())
          .Catch<Unit, Exception>(LogError<Unit>))
        .Concat() // Force sequential evaluation
        .Subscribe();
    }

    private IObservable<T> LogError<T>(Exception exception) {
      exception.Report("Sync error.", _user.DebugIdentifier);
      return Observable.Empty<T>();
    }

    private void Unsubscribe() {
      Log.Info($"{_user.Login}");

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
