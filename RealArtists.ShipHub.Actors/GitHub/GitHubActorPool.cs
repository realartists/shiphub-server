namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
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

    private void Remove(long userId) {
      lock (_lock) {
        if (_actorMap.ContainsKey(userId)) {
          _actorMap.Remove(userId);
        }
      }
    }

    private IGitHubActor GetRandomActor() {
      lock (this) {
        if (_actorMap.Count == 0) {
          throw new GitHubPoolEmptyException("No actors available.");
        }
        return _actorMap.Values[_random.Next(_actorMap.Count)];
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
              Remove(actor.GetPrimaryKeyLong());
              actor = null;
              break;
            default:
              return result;
          }
        } catch (GitHubRateException) {
          Remove(actor.GetPrimaryKeyLong());
          actor = null;
        } catch (InvalidOperationException) {
          // Grain activation failed
          Remove(actor.GetPrimaryKeyLong());
          actor = null;
        }
      }
    }

    public Task<GitHubResponse<Account>> User(string login, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.User(login, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Account>> User(long id, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.User(id, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Assignable(repoFullName, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<IssueComment>> IssueComment(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.IssueComment(repoFullName, commentId, cache, priority),
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

    public Task<GitHubResponse<IEnumerable<IssueComment>>> Comments(string repoFullName, DateTimeOffset since, uint maxPages, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Comments(repoFullName, since, maxPages, cache, priority),
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

    public Task<GitHubResponse<Account>> Organization(string login, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Organization(login, cache, priority),
        cacheOptions
      );
    }

    public Task<GitHubResponse<Account>> Organization(long id, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Organization(id, cache, priority),
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

    public Task<GitHubResponse<IDictionary<long, IEnumerable<Review>>>> PullRequestReviews(string repoFullName, IEnumerable<int> pullRequestNumbers, RequestPriority priority = RequestPriority.Background) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequestReviews(repoFullName, pullRequestNumbers, priority),
         null
       );
    }

    public Task<GitHubResponse<IEnumerable<PullRequest>>> PullRequests(string repoFullName, string sort, string direction, uint skipPages, uint maxPages, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequests(repoFullName, sort, direction, skipPages, maxPages, cache, priority),
         cacheOptions
       );
    }

    public Task<GitHubResponse<PullRequestComment>> PullRequestComment(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
         (actor, cache) => actor.PullRequestComment(repoFullName, commentId, cache, priority),
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

    public Task<GitHubResponse<Repository>> Repository(long repoId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return TryWithFallback(
        (actor, cache) => actor.Repository(repoId, cache, priority),
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
