namespace RealArtists.ShipHub.Api {
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Collections.Immutable;
  using System.Linq;
  using System.Threading.Tasks;
  using System.Web;
  using Common;
  using Controllers;
  using Sync;
  using System.Reactive;
  using System.Reactive.Linq;
  using System.Reactive.Subjects;
  using System.Reactive.Subjects;

  public class SyncManager {
    private class RepositoryReference {
      public long UserId { get; set; }
      public int MyProperty { get; set; }
    }

    // TODO: Finer grained locking.
    private object _theLock = new object();

    private Dictionary<long, HashSet<SyncConnection>> _userConnections = new Dictionary<long, HashSet<SyncConnection>>();
    private Dictionary<long, HashSet<long>> _repoUsers = new Dictionary<long, HashSet<long>>();
    private Dictionary<long, HashSet<long>> _orgUsers = new Dictionary<long, HashSet<long>>();

    private HashSet<long> _syncPending = new HashSet<long>();
    private HashSet<long> _syncPendingTemp = new HashSet<long>();

    private Subject<Unit> _syncSubject = new Subject<Unit>();


    public async Task SubscribeEvents() {
      // Subscribe to changes
    }
    private void SubscribeSync() {
      _syncSubject
        .Throttle(TimeSpan.FromSeconds(1))  // Throttle observes on its own thread.
        .SelectMany(_ => Observable.FromAsync(Sync))
        .Subscribe(
          _ => { },             // On next, do nothing.
          e => SubscribeSync(), // TODO: Log Error?
          SubscribeSync         // On completion, resubscribe (used by reload).
        );
    }

    public void AddConnection(SyncConnection connection) {
      // Register interest in orgs and repos here.
      // On change, remove and re-add.
      lock (_theLock) {
        _userConnections.Valn(connection.UserId).Add(connection);

        foreach (var repo in connection.SyncVersions.RepositoryVersions) {
          _repoUsers.Valn(repo.Key).Add(connection.UserId);
        }

        foreach (var org in connection.SyncVersions.OrgVersions) {
          _orgUsers.Valn(org.Key).Add(org.Value);
        }
      }
    }

    public void RemoveConnection(SyncConnection connection) {
      // TODO: Cleanup stale repo and org subscriptions

      lock (_theLock) {
        _userConnections.Val(connection.UserId)?.Remove(connection);
      }
    }

    public void MarkForSync(IEnumerable<long> userIds = null, IEnumerable<long> repoIds = null, IEnumerable<long> orgIds = null) {
      // In the end it's all userIds that map to connections.
      var uids = new HashSet<long>();

      if (userIds != null && userIds.Any()) {
        uids.UnionWith(userIds);
      }

      lock (_theLock) {
        if (repoIds != null && repoIds.Any()) {
          foreach (var repoId in repoIds) {
            if (_repoUsers.ContainsKey(repoId)) {
              uids.UnionWith(_repoUsers[repoId]);
            }
          }
        }

        if (orgIds != null && orgIds.Any()) {
          foreach (var orgId in orgIds) {
            if (_orgUsers.ContainsKey(orgId)) {
              uids.UnionWith(_orgUsers[orgId]);
            }
          }
        }
      }

      if (uids.Any()) {
        lock (_syncPending) {
          _syncPending.UnionWith(uids);
        }
      }
    }
  }
}
