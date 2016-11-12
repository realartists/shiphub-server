namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Orleans;

  public class GitHubActorPool : IGitHubPoolable {
    private IGrainFactory _grainFactory;
    private ConcurrentDictionary<long, IGitHubActor> _actorMap;
    private List<IGitHubActor> _actors;

    private object _lock = new object();
    private Random _random = new Random();

    public GitHubActorPool(IGrainFactory grainFactory, IEnumerable<long> userIds) {
      _grainFactory = grainFactory;
      _actorMap = new ConcurrentDictionary<long, IGitHubActor>(
        userIds.Select(x => new KeyValuePair<long, IGitHubActor>(x, _grainFactory.GetGrain<IGitHubActor>(x)))
      );
      _actors = _actorMap.Values.ToList();
    }

    public void Add(long userId) {
      var actor = _grainFactory.GetGrain<IGitHubActor>(userId);
      if (_actorMap.TryAdd(userId, actor)) {
        lock (_lock) {
          _actors.Add(actor);
        }
      }
    }

    private void Remove(IGitHubActor actor) {
      Remove(actor.GetPrimaryKeyLong());
    }

    private void Remove(long userId) {
      IGitHubActor actor = null;
      if (_actorMap.TryRemove(userId, out actor)) {
        lock (_lock) {
          _actors.Remove(actor);
        }
      }
    }

    private IGitHubActor GetRandomActor() {
      lock (_lock) {
        if (_actors.Count == 0) {
          throw new InvalidOperationException("No actors available.");
        }
        return _actors[_random.Next(_actors.Count)];
      }
    }

    private async Task<T> TryWithFallback<T>(Func<IGitHubActor, GitHubCacheDetails, Task<T>> action, GitHubCacheDetails cacheOptions)
      where T : GitHubResponse {
      IGitHubActor actor = null;
      if (cacheOptions?.UserId != null) {
        if (!_actorMap.TryGetValue(cacheOptions.UserId, out actor)) {
          cacheOptions = null;
        }
      }

      while (true) {
        if (actor == null) {
          actor = GetRandomActor();
        }

        try {
          var result = await action(actor, cacheOptions);
          if (result.IsError) {
            switch (result.ErrorSeverity) {
              // TODO: These may not be right.
              // For example, what if something is deleted, like a comment?
              case GitHubErrorSeverity.Abuse:
              case GitHubErrorSeverity.Failed:
              case GitHubErrorSeverity.RateLimited:
                Remove(actor);
                actor = null;
                continue;
              default:
                // Retry with someone else.
                continue;
            }
          }
          return result;
        } catch (GitHubException) {
          // TODO: Right now these are only thrown for pre-emptive rate limit aborts (before calling GitHub)
          // That may not always be the case, so think of a better way to indicate that condition.
          Remove(actor);
          actor = null;
        }
        // TODO: Also catch InvalidOperationException?
      }
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Assignable(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Comments(repoFullName, since, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Comments(repoFullName, issueNumber, since, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Commit(repoFullName, hash, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Events(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Issue(repoFullName, number, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.IssueCommentReactions(repoFullName, commentId, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.IssueReactions(repoFullName, issueNumber, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Issues(repoFullName, since, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Labels(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Milestones(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Account>> Organization(string orgName, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Organization(orgName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.OrganizationMembers(orgLogin, role, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequest(repoFullName, pullRequestNumber, cache),
         cacheOptions
       );
    }

    public Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.Repository(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.ListDirectoryContents(repoFullName, directoryPath, cache),
        cacheOptions
       );
    }

    public Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions = null) {
      return TryWithFallback(
        (actor, cache) => actor.FileContents(repoFullName, filePath, cache),
        cacheOptions
      );
    }
  }
}
