namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Orleans;

  public class GitHubActorPool : IGitHubPoolable {
    private IGrainFactory _grainFactory;

    private ConcurrentDictionary<long, IGitHubActor> _actorMap;
    private List<IGitHubActor> _actors;
    private Random _random = new Random();

    public GitHubActorPool(IGrainFactory grainFactory, IEnumerable<long> userIds) {
      _grainFactory = grainFactory;
      _actorMap = new ConcurrentDictionary<long, IGitHubActor>(
        userIds.Select(x => new KeyValuePair<long, IGitHubActor>(x, _grainFactory.GetGrain<IGitHubActor>(x)))
      );
      _actors = _actorMap.Values.ToList();
    }

    //public void Add(long userId) {
    //  var actor = _grainFactory.GetGrain<IGitHubActor>(userId);
    //  if (_actorMap.TryAdd(userId, actor)) {
    //    lock (this) {
    //      _actors.Add(actor);
    //    }
    //  }
    //}

    private void Remove(IGitHubActor actor) {
      var userId = actor.GetPrimaryKeyLong();
      if (_actorMap.TryRemove(userId, out var removed)) {
        lock (this) {
          _actors.Remove(removed);
        }
      }
    }

    private IGitHubActor GetRandomActor() {
      lock (this) {
        if (_actors.Count == 0) {
          throw new GitHubPoolEmptyException("No actors available.");
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

          // Only retry authorization failures and rate limiting
          switch (result.Status) {
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.Unauthorized:
              // Retry with someone else.
              Remove(actor);
              actor = null;
              continue;
          }

          return result;
        } catch (GitHubRateException) {
          Remove(actor);
          actor = null;
        } catch (InvalidOperationException) {
          // Grain activation failed
          Remove(actor);
          actor = null;
        }
      }
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Assignable(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Comments(repoFullName, since, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Comments(repoFullName, issueNumber, since, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Commit(repoFullName, hash, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Events(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Issue(repoFullName, number, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.IssueCommentReactions(repoFullName, commentId, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.IssueReactions(repoFullName, issueNumber, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset since, ushort maxPages, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Issues(repoFullName, since, maxPages, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Labels(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Milestones(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Account>> Organization(string orgName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Organization(orgName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.OrganizationMembers(orgLogin, role, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequest(repoFullName, pullRequestNumber, cache),
         cacheOptions
       );
    }

    public Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Repository(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.ListDirectoryContents(repoFullName, directoryPath, cache),
        cacheOptions
       );
    }

    public Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.FileContents(repoFullName, filePath, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Project>>> RepositoryProjects(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.RepositoryProjects(repoFullName, cache),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Project>>> OrganizationProjects(string organizationLogin, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.OrganizationProjects(organizationLogin, cache),
        cacheOptions
      );
    }
  }
}
