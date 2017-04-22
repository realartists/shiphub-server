namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Orleans;
  using Orleans.Concurrency;
  using QueueClient;
  using dm = Common.DataModel;
  using dmt = Common.DataModel.Types;

  public interface IGitHubClient {
    string AccessToken { get; }
    Uri ApiRoot { get; }
    ProductInfoHeaderValue UserAgent { get; }
    string UserInfo { get; }
    long UserId { get; }

    int NextRequestId();
  }

  [Reentrant]
  public class GitHubActor : Grain, IGitHubActor, IGitHubClient, IDisposable {
    public const int MaxConcurrentRequests = 2; // Also controls pagination fanout, if enabled.
    public const int PageSize = 100;
    public const bool InterpolationEnabled = false;

    // Should be less than Orleans timeout.
    // If changing, may also need to update values in CreateGitHubHttpClient()
    public static readonly TimeSpan GitHubRequestTimeout = OrleansAzureClient.ResponseTimeout.Subtract(TimeSpan.FromSeconds(2));

    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private IFactory<dm.ShipHubContext> _shipContextFactory;
    private IShipHubQueueClient _queueClient;
    private IShipHubConfiguration _configuration;

    public string AccessToken { get; private set; }
    public string Login { get; private set; }
    public long UserId { get; private set; }

    private DateTimeOffset? _dropRequestsUntil;
    private volatile bool _dropRequestAbuse;
    private volatile bool _lastRequestLimited;
    private volatile GitHubRateLimit _rateLimit; // Do not update directly please.

    // One queue for each priority
    private ConcurrentQueue<Func<Task>> _backgroundQueue = new ConcurrentQueue<Func<Task>>();
    private ConcurrentQueue<Func<Task>> _subRequestQueue = new ConcurrentQueue<Func<Task>>();
    private ConcurrentQueue<Func<Task>> _interactiveQueue = new ConcurrentQueue<Func<Task>>();

    public Uri ApiRoot { get; }
    public ProductInfoHeaderValue UserAgent { get; } = new ProductInfoHeaderValue(ApplicationName, ApplicationVersion);
    public string UserInfo => $"{UserId} {Login}";

    private static IGitHubHandler SharedHandler;
    private static void EnsureHandlerPipelineCreated(Uri apiRoot) {
      if (SharedHandler != null) {
        return;
      }

      // Set the maximum number of concurrent connections
      HttpUtilities.SetServicePointConnectionLimit(apiRoot);

      SharedHandler = new GitHubHandler();
    }

    public GitHubActor(IFactory<dm.ShipHubContext> shipContextFactory, IShipHubQueueClient queueClient, IShipHubConfiguration configuration) {
      _shipContextFactory = shipContextFactory;
      _queueClient = queueClient;
      _configuration = configuration;

      ApiRoot = _configuration.GitHubApiRoot;
      EnsureHandlerPipelineCreated(ApiRoot);
    }

    public override async Task OnActivateAsync() {
      UserId = this.GetPrimaryKeyLong();

      using (var context = _shipContextFactory.CreateInstance()) {
        // Require user and token to already exist in database.
        var user = await context.Users
          .Include(x => x.Tokens)
          .SingleOrDefaultAsync(x => x.Id == UserId);

        if (user == null) {
          throw new InvalidOperationException($"User {UserId} does not exist.");
        }

        if (!user.Tokens.Any()) {
          throw new InvalidOperationException($"User {UserId} has no token.");
        }

        AccessToken = user.Tokens.First().Token;
        Login = user.Login;

        if (user.RateLimitReset != EpochUtility.EpochOffset) {
          UpdateRateLimit(new GitHubRateLimit(
            AccessToken,
            user.RateLimit,
            user.RateLimitRemaining,
            user.RateLimitReset));
        }
      }

      await base.OnActivateAsync();
    }

    public override async Task OnDeactivateAsync() {
      using (var context = _shipContextFactory.CreateInstance()) {
        await context.UpdateRateLimit(_rateLimit);
      }

      Dispose();

      await base.OnDeactivateAsync();
    }

    ////////////////////////////////////////////////////////////
    // Helpers
    ////////////////////////////////////////////////////////////

    private int _requestId = 0;
    public int NextRequestId() {
      return Interlocked.Increment(ref _requestId);
    }

    private void UpdateRateLimit(GitHubRateLimit rateLimit) {
      lock (this) {
        if (_rateLimit == null
          || _rateLimit.Reset < rateLimit.Reset
          || _rateLimit.Remaining > rateLimit.Remaining) {
          _rateLimit = rateLimit;
        }
      }
    }

    ////////////////////////////////////////////////////////////
    // GitHub Actions
    ////////////////////////////////////////////////////////////

    public Task<GitHubResponse<Account>> User(GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest("user", cacheOptions, priority);
      return EnqueueRequest<Account>(request);
    }

    public Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest("user/emails", cacheOptions, priority);
      return FetchPaged(request, (UserEmail x) => x.Email);
    }

    public Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"/repos/{repoFullName}", cacheOptions, priority);
      return EnqueueRequest<Repository>(request);
    }

    public Task<GitHubResponse<IEnumerable<Repository>>> Repositories(GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest("user/repos", cacheOptions, priority);
      return FetchPaged(request, (Repository x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, long issueId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/timeline", cacheOptions, priority) {
        // Timeline support (application/vnd.github.mockingbird-preview+json)
        // https://developer.github.com/changes/2016-05-23-timeline-preview-api/
        AcceptHeaderOverride = "application/vnd.github.mockingbird-preview+json",
      };
      // Ugh. Uniqueness here is hard because IDs aren't.
      return FetchPaged(request, (IssueEvent x) => {
        // Fixup since GitHub doesn't consistently send it
        x.FallbackIssueId = issueId;
        return x.UniqueKey;
      });
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{number}", cacheOptions, priority) {
        // https://developer.github.com/v3/issues/#reactions-summary 
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      return EnqueueRequest<Issue>(request);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset since, uint maxPages, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues", cacheOptions, priority) {
        // https://developer.github.com/v3/issues/#reactions-summary 
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      request.AddParameter("state", "all");
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      request.AddParameter("since", since);

      return FetchPaged(request, (Issue x) => x.Id, maxPages);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/reactions", cacheOptions, priority) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments/{commentId}/reactions", cacheOptions, priority) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<IssueComment>>> IssueComments(string repoFullName, int issueNumber, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/comments", cacheOptions, priority) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      if (since != null) {
        request.AddParameter("since", since);
      }
      return FetchPaged(request, (IssueComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<IssueComment>>> IssueComments(string repoFullName, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments", cacheOptions, priority) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (IssueComment x) => x.Id);
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/commits/{hash}", cacheOptions, priority);
      return EnqueueRequest<Commit>(request);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/events", cacheOptions, priority);
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (IssueEvent x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/labels", cacheOptions, priority);
      return FetchPaged(request, (Label x) => Tuple.Create(x.Name, x.Color));
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/milestones", cacheOptions, priority);
      request.AddParameter("state", "all");
      return FetchPaged(request, (Milestone x) => x.Id);
    }

    public Task<GitHubResponse<Account>> Organization(string orgName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"orgs/{orgName}", cacheOptions, priority);
      return EnqueueRequest<Account>(request);
    }

    public async Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(string state, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest("user/memberships/orgs", cacheOptions, priority);
      request.AddParameter(nameof(state), state);
      var result = await FetchPaged(request, (OrganizationMembership x) => x.Organization.Id);

      if (result.IsOk) {
        // Seriously GitHub?
        foreach (var membership in result.Result) {
          membership.Organization.Type = GitHubAccountType.Organization;
        }
      }

      return result;
    }

    public Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      // defaults: filter=all, role=all
      var request = new GitHubRequest($"orgs/{orgLogin}/members", cacheOptions, priority);
      request.AddParameter("role", role);
      return FetchPaged(request, (Account x) => x.Id);
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/{pullRequestNumber}", cacheOptions, priority) {
        // Pull Request Reviews are in beta
        AcceptHeaderOverride = "application/vnd.github.black-cat-preview+json",
      };
      return EnqueueRequest<PullRequest>(request);
    }

    public Task<GitHubResponse<IEnumerable<PullRequest>>> PullRequests(string repoFullName, string sort, string direction, uint skipPages, uint maxPages, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls", cacheOptions, priority) {
        // Pull Request Reviews are in beta
        AcceptHeaderOverride = "application/vnd.github.black-cat-preview+json",
      };
      request.AddParameter("state", "all");
      request.AddParameter("sort", sort);
      request.AddParameter("direction", direction);
      return FetchPaged(request, (PullRequest x) => x.Id, maxPages, skipPages);
    }

    public Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestComments(string repoFullName, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/comments", cacheOptions, priority) {
        // https://developer.github.com/v3/pulls/comments/#list-comments-in-a-repository
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (PullRequestComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestComments(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/{pullRequestNumber}/comments", cacheOptions, priority) {
        // https://developer.github.com/v3/pulls/comments/#list-comments-on-a-pull-request
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      return FetchPaged(request, (PullRequestComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Review>>> PullRequestReviews(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/{pullRequestNumber}/reviews", cacheOptions, priority) {
        // https://developer.github.com/v3/pulls/reviews/#list-reviews-on-a-pull-request
        AcceptHeaderOverride = "application/vnd.github.black-cat-preview+json"
      };
      return FetchPaged(request, (Review x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestReviewComments(string repoFullName, int pullRequestNumber, long pullRequestReviewId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/{pullRequestNumber}/reviews/{pullRequestReviewId}/comments", cacheOptions, priority) {
        // https://developer.github.com/v3/pulls/reviews/#get-comments-for-a-single-review
        // NOTE: Not currently possible to get reactions
        AcceptHeaderOverride = "application/vnd.github.black-cat-preview+json"
      };
      return FetchPaged(request, (PullRequestComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/assignees", cacheOptions, priority);
      return FetchPaged(request, (Account x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"orgs/{name}/hooks", cacheOptions, priority);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/hooks", cacheOptions, priority);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook, RequestPriority priority) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"repos/{repoFullName}/hooks", hook, priority);
      return EnqueueRequest<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook, RequestPriority priority) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"orgs/{orgName}/hooks", hook, priority);
      return EnqueueRequest<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId, RequestPriority priority) {
      var request = new GitHubRequest(HttpMethod.Delete, $"repos/{repoFullName}/hooks/{hookId}", priority);
      return EnqueueRequest<bool>(request);
    }

    public Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId, RequestPriority priority) {
      var request = new GitHubRequest(HttpMethod.Delete, $"orgs/{orgName}/hooks/{hookId}", priority);
      return EnqueueRequest<bool>(request);
    }

    public Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, IEnumerable<string> events, RequestPriority priority) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"repos/{repoFullName}/hooks/{hookId}",
        new {
          Events = events,
        },
        priority);

      return EnqueueRequest<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, IEnumerable<string> events, RequestPriority priority) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"orgs/{orgName}/hooks/{hookId}",
        new {
          Events = events,
        },
        priority);

      return EnqueueRequest<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId, RequestPriority priority) {
      var request = new GitHubRequest(HttpMethod.Post, $"orgs/{name}/hooks/{hookId}/pings", priority);
      return EnqueueRequest<bool>(request);
    }

    public Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId, RequestPriority priority) {
      var request = new GitHubRequest(HttpMethod.Post, $"repos/{repoFullName}/hooks/{hookId}/pings", priority);
      return EnqueueRequest<bool>(request);
    }

    public Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      if (!directoryPath.StartsWith("/", StringComparison.Ordinal)) {
        directoryPath = "/" + directoryPath;
      }
      var request = new GitHubRequest($"repos/{repoFullName}/contents{directoryPath}", cacheOptions, priority);
      return FetchPaged(request, (ContentsFile f) => f.Path);
    }

    public Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      if (!filePath.StartsWith("/", StringComparison.Ordinal)) {
        filePath = "/" + filePath;
      }
      var request = new GitHubRequest($"repos/{repoFullName}/contents{filePath}", cacheOptions, priority);
      return EnqueueRequest<byte[]>(request);
    }

    private Task<GitHubResponse<IEnumerable<Project>>> Projects(string endpoint, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest(endpoint, cacheOptions, priority) {
        AcceptHeaderOverride = "application/vnd.github.inertia-preview+json",
      };
      return FetchPaged(request, (Project p) => p.Id);
    }

    public Task<GitHubResponse<IEnumerable<Project>>> RepositoryProjects(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return Projects($"repos/{repoFullName}/projects", cacheOptions, priority);
    }

    public Task<GitHubResponse<IEnumerable<Project>>> OrganizationProjects(string organizationLogin, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return Projects($"orgs/{organizationLogin}/projects", cacheOptions, priority);
    }

    ////////////////////////////////////////////////////////////
    // Handling
    ////////////////////////////////////////////////////////////

    private SemaphoreSlim _maxConcurrentRequests = new SemaphoreSlim(MaxConcurrentRequests);

    private Task<GitHubResponse<T>> EnqueueRequest<T>(GitHubRequest request) {
      var completionTask = new TaskCompletionSource<GitHubResponse<T>>();
      Func<Task> executeRequestTask = async () => {
        try {
          /* ALL RATE LIMIT ENFORCEMENT SHOULD EXIST HERE ONLY
           * We need one central place for this to maintain sanity.
           * 
           * So when we *first* hit a rate limit or abuse warning, log the exception.
           * Then throw but don't log until a request succeeded. Upstream callers are
           * expected to act (if needed) on the exceptions, but silently swallow them.
           * 
           * This should ensure we're aware of all rate and abuse issues, without
           * overwhelming us with non-actionable exceptions.
           */

          if (_dropRequestsUntil != null && _dropRequestsUntil.Value > DateTimeOffset.UtcNow) {
            var rateException = new GitHubRateException(UserInfo, request.Uri, _rateLimit, _dropRequestAbuse);
            if (!_lastRequestLimited) {
              _lastRequestLimited = true;
              rateException.Report(userInfo: UserInfo);
            }
            throw rateException;
          }

          var totalWait = Stopwatch.StartNew();
          try {
            GitHubResponse<T> result;
            using (var timeout = new CancellationTokenSource(GitHubRequestTimeout)) {
              result = await SharedHandler.Fetch<T>(this, request, timeout.Token);
            }
            await PostProcessResponse(result);
            completionTask.SetResult(result);
          } catch (TaskCanceledException exception) {
            totalWait.Stop();
            exception.Report($"GitHub Request Timeout after {totalWait.ElapsedMilliseconds}ms for [{request.Uri}]");
            throw;
          }
        } catch (Exception e) {
          completionTask.SetException(e);
        }
      };

      // Enqueue the request
      switch (request.Priority) {
        case RequestPriority.Interactive:
          _interactiveQueue.Enqueue(executeRequestTask);
          break;
        case RequestPriority.SubRequest:
          _subRequestQueue.Enqueue(executeRequestTask);
          break;
        case RequestPriority.Background:
        case RequestPriority.Unspecified:
        default:
          _backgroundQueue.Enqueue(executeRequestTask);
          break;
      }

      // Start dispatcher if needed.
      if (_maxConcurrentRequests.Wait(TimeSpan.Zero)) {
        DispatchRequests().LogFailure(UserInfo);
      }

      return completionTask.Task;
    }

    private async Task DispatchRequests() {
      try {
        while (true) {
          var nextRequest = DequeueNextRequest();
          if (nextRequest == null) {
            break;
          }

          await nextRequest();
        }
      } finally {
        _maxConcurrentRequests.Release();
      }
    }

    private Func<Task> DequeueNextRequest() {
      // Get next request by priority
      if (_interactiveQueue.TryDequeue(out var task)
        || _subRequestQueue.TryDequeue(out task)
        || _backgroundQueue.TryDequeue(out task)) {
        // TODO: Drop super old requests (hours)
        return task;
      }

      return null;
    }

    private async Task PostProcessResponse(GitHubResponse response) {
      // Token revocation handling and abuse.
      var abuse = false;
      DateTimeOffset? limitUntil = null;
      if (response?.Status == HttpStatusCode.Unauthorized) {
        using (var ctx = _shipContextFactory.CreateInstance()) {
          var changes = await ctx.RevokeAccessTokens(UserId);
          await _queueClient.NotifyChanges(changes);
        }
        DeactivateOnIdle();
      } else if (response.Error?.IsAbuse == true) {
        abuse = true;
        limitUntil = response.RetryAfter ?? DateTimeOffset.UtcNow.AddSeconds(60); // Default to 60 seconds.
      } else if (_rateLimit?.IsExceeded == true) {
        limitUntil = _rateLimit.Reset;
      }

      if (limitUntil != null) {
        // Don't set _lastRequestLimited until an error is logged
        _dropRequestAbuse = abuse;
        _dropRequestsUntil = limitUntil;
        using (var context = _shipContextFactory.CreateInstance()) {
          var oldRate = _rateLimit;
          var newRate = new GitHubRateLimit(
            oldRate.AccessToken,
            oldRate.Limit,
            Math.Min(oldRate.Remaining, GitHubRateLimit.RateLimitFloor - 1),
            limitUntil.Value);
          UpdateRateLimit(newRate);

          // Record in DB for sync notification
          await context.UpdateRateLimit(newRate);
        }

        // Force sync notification
        var changes = new dmt.ChangeSummary();
        changes.Users.Add(UserId);
        await _queueClient.NotifyChanges(changes);
      } else if (response.RateLimit != null) {
        _lastRequestLimited = false;
        // Normal rate limit tracking
        UpdateRateLimit(response.RateLimit);
      }
    }

    private async Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubRequest request, Func<T, TKey> keySelector, uint? maxPages = null, uint? skipPages = null) {
      if (request.Method != HttpMethod.Get) {
        throw new InvalidOperationException("Only GETs can be paginated.");
      }
      if (maxPages == 0) {
        throw new InvalidOperationException($"{nameof(maxPages)} must be omitted or greater than 0");
      }

      // Always request the largest page size
      if (!request.Parameters.ContainsKey("per_page")) {
        request.AddParameter("per_page", PageSize);
      }

      // In all cases we need the first page 🙄
      var response = await EnqueueRequest<IEnumerable<T>>(request);

      // When successful, try to enumerate. Else immediately return the error.
      if (response.IsOk) {
        // If skipping pages, calculate here.
        switch (skipPages) {
          case null:
          case 0:
            break;
          case 1 when response.Pagination?.Next != null:
            var nextUri = response.Pagination.Next;
            response = await EnqueueRequest<IEnumerable<T>>(response.Request.CloneWithNewUri(nextUri));
            break;
          case 1: // response.Pagination == null
            response.Result = Array.Empty<T>();
            break;
          case uint skip: // skipPages > 1
            if (response.Pagination?.CanInterpolate != true) {
              throw new InvalidOperationException($"Skipping pages is not supported for [{response.Request.Uri}]: {response.Pagination?.SerializeObject()}");
            }
            nextUri = response.Pagination.Interpolate().Skip((int)(skip - 1)).FirstOrDefault();
            if (nextUri == null) {
              // We skipped more pages than existed.
              response.Pagination = null;
              response.Result = Array.Empty<T>();
            }
            break;
          default:
            break;
        }

        // Now, if there's more to do, enumerate the results
        if ((maxPages == null || maxPages > 1) && response.Pagination?.Next != null) {
          // By default, upgrade background => subrequest
          var subRequestPriority = RequestPriority.SubRequest;
          // Ensure interactive => interactive
          if (response.Request.Priority == RequestPriority.Interactive) {
            subRequestPriority = RequestPriority.Interactive;
          }

          if (InterpolationEnabled && response.Pagination.CanInterpolate) {
            response = await EnumerateParallel(response, subRequestPriority, maxPages);
          } else { // Walk in order
            response = await EnumerateSequential(response, subRequestPriority, maxPages);
          }
        }
      }

      // Response should have:
      // 1) Pagination header from last page
      // 2) Cache data from first page, IIF it's a complete result, and not truncated due to errors.
      // 3) Number of pages returned

      return response.Distinct(keySelector);
    }

    private async Task<GitHubResponse<IEnumerable<TItem>>> EnumerateParallel<TItem>(GitHubResponse<IEnumerable<TItem>> firstPage, RequestPriority priority, uint? maxPages) {
      var partial = false;
      var results = new List<TItem>(firstPage.Result);
      uint resultPages = 1;
      var pages = firstPage.Pagination.Interpolate();

      if (maxPages < pages.Count()) {
        partial = true;
        pages = pages.Take((int)maxPages - 1);
      }

      var pageRequestors = pages
        .Select(page => {
          Func<Task<GitHubResponse<IEnumerable<TItem>>>> requestor = () => {
            var request = firstPage.Request.CloneWithNewUri(page);
            request.Priority = priority;
            return EnqueueRequest<IEnumerable<TItem>>(request);
          };

          return requestor;
        }).ToArray();

      // Check if we can request all the pages within the limit.
      if (firstPage.RateLimit.Remaining < pageRequestors.Length) {
        firstPage.Result = default(IEnumerable<TItem>);
        firstPage.Status = HttpStatusCode.Forbidden; // Rate Limited
        return firstPage;
      }

      var abort = false;
      var batch = new List<GitHubResponse<IEnumerable<TItem>>>();
      // TODO: Cancellation (for when errors are encountered)?
      for (var i = 0; !abort && i < pageRequestors.Length;) {
        var tasks = new List<Task<GitHubResponse<IEnumerable<TItem>>>>();
        for (var j = 0; j < MaxConcurrentRequests && i < pageRequestors.Length; ++i, ++j) {
          tasks.Add(pageRequestors[i]());
        }
        await Task.WhenAll(tasks);
        foreach (var task in tasks) {
          if (task.IsFaulted) {
            task.Wait(); // force exception to throw
          } else {
            var part = task.Result;
            batch.Add(part);
            if (!part.IsOk) {
              // Return error immediately.
              abort = true;
            }
          }
        }
      }

      foreach (var response in batch) {
        if (response.IsOk) {
          ++resultPages;
          results.AddRange(response.Result);
        } else if (maxPages != null) {
          // Return results up to this point.
          partial = true;
          break;
        } else {
          return response;
        }
      }

      // Keep cache and other headers from first page.
      var result = firstPage;
      result.Result = results;
      result.Pages = resultPages;

      var rates = batch
        .Where(x => x.RateLimit != null)
        .Select(x => x.RateLimit)
        .GroupBy(x => x.Reset)
        .OrderByDescending(x => x.Key)
        .First();
      result.RateLimit = new GitHubRateLimit(
        firstPage.RateLimit.AccessToken,
        rates.Min(x => x.Limit),
        rates.Min(x => x.Remaining),
        rates.Key);

      // Don't return cache data for partial results
      if (partial) {
        result.CacheData = null;
      }

      return result;
    }

    private async Task<GitHubResponse<IEnumerable<TItem>>> EnumerateSequential<TItem>(GitHubResponse<IEnumerable<TItem>> firstPage, RequestPriority priority, uint? maxPages) {
      var partial = false;
      var results = new List<TItem>(firstPage.Result);

      // Walks pages in order, one at a time.
      var current = firstPage;
      uint page = 1;
      while (current.Pagination?.Next != null && page < maxPages) {
        var nextReq = current.Request.CloneWithNewUri(current.Pagination.Next);
        nextReq.Priority = priority;
        current = await EnqueueRequest<IEnumerable<TItem>>(nextReq);

        if (current.IsOk) {
          results.AddRange(current.Result);
        } else if (maxPages != null) {
          // Return results up to this point.
          partial = true;
          break;
        } else {
          // On error, return the error
          return current;
        }

        ++page;
      }

      // Keep cache from the first page, rate + pagination from the last.
      var result = firstPage;
      result.Result = results;
      result.Pages = page;
      result.RateLimit = current.RateLimit;

      // Don't return cache data for partial results
      if (partial) {
        result.CacheData = null;
      }

      return result;
    }

    ////////////////////////////////////////////////////////////
    // IDisposable
    ////////////////////////////////////////////////////////////

    private bool disposedValue = false;
    protected virtual void Dispose(bool disposing) {
      if (!disposedValue) {
        if (disposing) {
          if (_maxConcurrentRequests != null) {
            _maxConcurrentRequests.Dispose();
            _maxConcurrentRequests = null;
          }
        }
        disposedValue = true;
      }
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }
  }
}
