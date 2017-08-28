namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Collections.Immutable;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using Orleans.Concurrency;
  using dm = Common.DataModel;

  public class ThingsAndStuff : IActorPool {
    private IFactory<dm.ShipHubContext> _contextFactory;

    public ThingsAndStuff(IFactory<dm.ShipHubContext> contextFactory) {
      _contextFactory = contextFactory;
    }

    public Task Reload() {
      throw new NotImplementedException();
    }

    public Task<T> TryWithFallback<T>(Func<IGitHubActor, GitHubCacheDetails, Task<T>> action, GitHubCacheDetails cacheOptions) {
      throw new NotImplementedException();
    }
  }

  [Reentrant]
  [StatelessWorker(1)]
  public class GitHubPublicPoolActor : AbstractGitHubPool, IGitHubPublicPoolActor, IActorPool {
    private const long ReloadEvery = 250000;

    private IGrainFactory _grainFactory;
    private IFactory<dm.ShipHubContext> _contextFactory;

    // Access interlocked, skips bad
    private long _counter = 0;
    private int _index = 0;

    private object _lock = new object();
    private long[] _userIds;
    private ImmutableHashSet<long> _userSet;

    public GitHubPublicPoolActor(IGrainFactory grainFactory, IFactory<dm.ShipHubContext> contextFactory)
      : base(new ThingsAndStuff(contextFactory)) {
      _grainFactory = grainFactory;
      _contextFactory = contextFactory;
    }

    public override async Task OnActivateAsync() {
      // Load all the userIds
      await PopulateUsers();

      await base.OnActivateAsync();
    }

    private async Task PopulateUsers() {
      long[] userIds;
      using (var context = _contextFactory.CreateInstance()) {
        // Just load 'em all. We'll remove the revoked ones.
        userIds = await context.Tokens
          .AsNoTracking()
          .Select(x => x.UserId)
          .Distinct()
          .ToArrayAsync();
      }

      lock (_lock) {
        _userIds = userIds;
        _userSet = _userIds.ToImmutableHashSet();
      }
    }

    private void RemoveUser(int index, long userId) {
      lock (_lock) {
        if (index > 0) {
          if (_userIds[index] != userId) {
            throw new InvalidOperationException($"UserId at index {index} is {_userIds[index]} and does not match {nameof(userId)} {userId}.");
          }
          _userIds[index] = 0;
        }
        _userSet = _userSet.Remove(userId);
      }
    }

    private async Task<T> TryWithFallback<T>(Func<IGitHubActor, GitHubCacheDetails, Task<T>> action, GitHubCacheDetails cacheOptions)
      where T : GitHubResponse {
      var count = Interlocked.Increment(ref _counter);

      // Check if we should reload user list
      // This is gross, but ensures we don't get stuck and cleans gaps
      if ((count % ReloadEvery) == 0) {
        await PopulateUsers();
      }

      // Our working copies
      var userIds = _userIds;
      var userSet = _userSet;

      IGitHubActor actor = null;

      // Try the actor from the cache data first.
      // Also not ideal - can waste a fair amount of time if the user has no token :(
      if (cacheOptions?.UserId != null) {
        var userId = cacheOptions.UserId;
        if (userSet.Contains(userId)) {
          actor = _grainFactory.GetGrain<IGitHubActor>(userId);
        } else {
          cacheOptions = null;
        }
      }

      while (true) {
        // Check if the pool is empty (can happen on dev machines, less so live)
        if (userIds.Length == 0) {
          throw new GitHubPoolEmptyException("No public actors available.");
        }

        var index = -1;
        if (actor == null) {
          index = Interlocked.Increment(ref _index);
          var userId = _userIds[index];
          if (userId == 0) {
            continue;
          }
          actor = _grainFactory.GetGrain<IGitHubActor>(userId);
        }

        try {
          var result = await action(actor, cacheOptions);

          // Only retry authorization failures and rate limiting
          switch (result.Status) {
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.Unauthorized:
              // Retry with someone else.
              RemoveUser(index, actor.GetPrimaryKeyLong());
              actor = null;
              break;
            default:
              return result;
          }
        } catch (GitHubRateException) {
          RemoveUser(index, actor.GetPrimaryKeyLong());
          actor = null;
        } catch (InvalidOperationException) {
          // Grain activation failed
          RemoveUser(index, actor.GetPrimaryKeyLong());
          actor = null;
        }
      }
    }
  }
}
