namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Newtonsoft.Json.Linq;
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
              break;
            default:
              return result;
          }
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
        (actor, cache) => actor.Assignable(repoFullName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<IssueComment>>> IssueComments(string repoFullName, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.IssueComments(repoFullName, since, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<IssueComment>>> IssueComments(string repoFullName, int issueNumber, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.IssueComments(repoFullName, issueNumber, since, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Commit(repoFullName, hash, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<CommitComment>>> CommitComments(string repoFullName, string reference, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.CommitComments(repoFullName, reference, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> CommitCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.CommitCommentReactions(repoFullName, commentId, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<CommitStatus>>> CommitStatuses(string repoFullName, string reference, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.CommitStatuses(repoFullName, reference, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IDictionary<string, JToken>>> BranchProtection(string repoFullName, string branchName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.BranchProtection(repoFullName, branchName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Events(repoFullName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Issue(repoFullName, number, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.IssueCommentReactions(repoFullName, commentId, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.IssueReactions(repoFullName, issueNumber, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset since, uint maxPages, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Issues(repoFullName, since, maxPages, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> NewestIssues(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.NewestIssues(repoFullName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Labels(repoFullName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Milestones(repoFullName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Account>> Organization(string orgName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Organization(orgName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.OrganizationMembers(orgLogin, role, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequest(repoFullName, pullRequestNumber, cache, priority),
         cacheOptions
       );
    }

    public Task<GitHubResponse<IEnumerable<PullRequest>>> PullRequests(string repoFullName, string sort, string direction, uint skipPages, uint maxPages, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequests(repoFullName, sort, direction, skipPages, maxPages, cache, priority),
         cacheOptions
       );
    }

    public Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestComments(string repoFullName, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequestComments(repoFullName, since, cache, priority),
         cacheOptions
       );
    }

    public Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestComments(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequestComments(repoFullName, pullRequestNumber, cache, priority),
         cacheOptions
       );
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> PullRequestCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequestCommentReactions(repoFullName, commentId, cache, priority),
         cacheOptions
       );
    }

    public Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Repository(repoFullName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.ListDirectoryContents(repoFullName, directoryPath, cache, priority),
        cacheOptions
       );
    }

    public Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.FileContents(repoFullName, filePath, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Project>>> RepositoryProjects(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.RepositoryProjects(repoFullName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Project>>> OrganizationProjects(string organizationLogin, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.OrganizationProjects(organizationLogin, cache, priority),
        cacheOptions
      );
    }
  }
}
