namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using ActorInterfaces.GitHub;
  using Orleans;

  public class GitHubActorPool : AbstractActorPool {
    private IGrainFactory _grainFactory;

    private object _lock = new object();
    private SortedList<long, IGitHubActor> _actorMap;

    private Random _random = new Random();

    public GitHubActorPool(IGrainFactory grainFactory, IEnumerable<long> userIds) {
      if (userIds == null || userIds?.Any() != true) {
        throw new ArgumentException("Cannot be null or empty.", nameof(userIds));
      }
      _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
      _actorMap = new SortedList<long, IGitHubActor>(userIds.ToDictionary(x => x, x => _grainFactory.GetGrain<IGitHubActor>(x)));
    }

    public void Add(long userId) {
      lock (_lock) {
        if (!_actorMap.ContainsKey(userId)) {
          _actorMap.Add(userId, _grainFactory.GetGrain<IGitHubActor>(userId));
        }
      }
    }

    public void Add(IEnumerable<long> userIds) {
      lock (_lock) {
        foreach (var userId in userIds) {
          if (!_actorMap.ContainsKey(userId)) {
            _actorMap.Add(userId, _grainFactory.GetGrain<IGitHubActor>(userId));
          }
        }
      }
    }

    protected override void Remove(long userId) {
      lock (_lock) {
        if (_actorMap.ContainsKey(userId)) {
          _actorMap.Remove(userId);
        }
      }
    }

    protected override IGitHubActor GetActor() {
      lock (this) {
        if (_actorMap.Count == 0) {
          throw new GitHubPoolEmptyException("No actors available.");
        }
        return _actorMap.Values[_random.Next(_actorMap.Count)];
      }
    }

    protected override IGitHubActor GetActor(long userId) {
      IGitHubActor actor = null;
      lock (this) {
        _actorMap.TryGetValue(userId, out actor);
      }
      return actor;
    }
  }
}
