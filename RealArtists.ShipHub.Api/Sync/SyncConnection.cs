﻿namespace RealArtists.ShipHub.Api.Sync {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
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
  using Common.WebSockets;
  using Filters;
  using Messages;
  using Microsoft.ApplicationInsights;
  using Mindscape.Raygun4Net.WebApi;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using QueueClient;

  public interface ISyncConnection {
    Task SendJsonAsync(object message);
    Task CloseAsync();
  }

  public class SyncConnection : WebSocketHandler, ISyncConnection {
    private const int _MaxMessageSize = 64 * 1024; // 64 KB
    private static readonly Guid _purgeId = new Guid("1789F841-6DC1-4719-9A56-A05908D00000");

    // TODO: Fix for production.
    private static readonly IObservable<long> _PollInterval =
      Observable.Interval(TimeSpan.FromMinutes(2))
      .Publish()
      .RefCount();

    private ShipHubPrincipal _user;
    private SyncContext _syncContext;
    private ISyncManager _syncManager;
    private IShipHubQueueClient _queueClient;
    private IGrainFactory _grainFactory;

    private IDisposable _syncSubscription;
    private IDisposable _pollSubscription;


    public SyncConnection(ShipHubPrincipal user, ISyncManager syncManager, IShipHubQueueClient queueClient, IGrainFactory grainFactory)
      : base(_MaxMessageSize) {
      _user = user;
      _syncManager = syncManager;
      _queueClient = queueClient;
      _grainFactory = grainFactory;
    }

    public override Task OnError(Exception exception) {
      try {
        // TODO: This should not be needed, but I'm seeing weird behavior without it. Look into this later?
        Unsubscribe();
      } finally {
        // Ensure we log the original error

        exception = exception.Simplify();
        var userInfo = _user.DebugIdentifier;

        // HACK: This is gross. Find a way to inject these or something.
        // No need to cache since connection closed immediately after this.
        var raygunClient = new RaygunWebApiClient() {
          User = userInfo,
        };
        raygunClient.AddWrapperExceptions(typeof(AggregateException));
        raygunClient.Send(exception);

        var aiClient = new TelemetryClient();
        aiClient.TrackException(exception, new Dictionary<string, string>() {
          { "User", userInfo },
        });
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
      var data = jobj.ToObject<SyncMessageBase>(JsonUtility.SaneSerializer);
      switch (data.MessageType) {
        case "hello":
          if (_syncContext != null) {
            throw new InvalidOperationException("hello can only be sent once at initial connection of websocket.");
          }

          // parse message, update local versions
          var hello = jobj.ToObject<HelloRequest>(JsonUtility.SaneSerializer);

          // first respond with purgeIdentifier
          await SendJsonAsync(new HelloResponse() { PurgeIdentifier = _purgeId });

          // now start sync
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
          await _queueClient.SyncRepositoryIssueTimeline(repoFullName, issueNumber, _user.UserId);
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
          JsonUtility.SaneSerializer.Serialize(sw, message);
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
        .Concat() // Force sequential evaluation
        .Subscribe();

      // Polling for updates
      var userActor = _grainFactory.GetGrain<IUserActor>(_user.UserId);
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
