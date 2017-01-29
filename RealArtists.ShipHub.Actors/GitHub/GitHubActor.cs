namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Configuration;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Text.RegularExpressions;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Orleans;
  using Orleans.CodeGeneration;
  using Orleans.Concurrency;
  using QueueClient;
  using dm = Common.DataModel;

  public interface IGitHubClient {
    string AccessToken { get; }
    Uri ApiRoot { get; }
    Guid CorrelationId { get; }
    ProductInfoHeaderValue UserAgent { get; }
    string UserInfo { get; }
    long UserId { get; }

    int NextRequestId();
  }

  [Reentrant]
  public class GitHubActor : Grain, IGitHubActor, IGitHubClient, IDisposable {
    public const int MaxConcurrentRequests = 4;
    public const int PageSize = 100;
    public const bool InterpolationEnabled = true;

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

    public Uri ApiRoot { get; }
    public ProductInfoHeaderValue UserAgent { get; } = new ProductInfoHeaderValue(ApplicationName, ApplicationVersion);
    public string UserInfo { get { return $"{UserId} ({Login})"; } }
    // TODO: Orleans has a concept of state/correlation that we can use
    // instead of Guid.NewGuid() or adding parameters to every call.
    public Guid CorrelationId { get; } = Guid.NewGuid();

    private static IGitHubHandler SharedHandler;
    private static void EnsureHandlerPipelineCreated(Uri apiRoot, IFactory<dm.ShipHubContext> shipContextFactory) {
      if (SharedHandler != null) {
        return;
      }
      
      // Set the maximum number of concurrent connections
      HttpUtilities.SetServicePointConnectionLimit(apiRoot);

      SharedHandler = new SneakyCacheFilter(new GitHubHandler(), shipContextFactory);
    }

    public GitHubActor(IFactory<dm.ShipHubContext> shipContextFactory, IShipHubQueueClient queueClient, IShipHubConfiguration configuration) {
      _shipContextFactory = shipContextFactory;
      _queueClient = queueClient;
      _configuration = configuration;

      ApiRoot = _configuration.GitHubApiRoot;
      EnsureHandlerPipelineCreated(ApiRoot, _shipContextFactory);
    }

    public override async Task OnActivateAsync() {
      UserId = this.GetPrimaryKeyLong();

      using (var context = _shipContextFactory.CreateInstance()) {
        // Require user and token to already exist in database.
        var user = await context.Users.SingleOrDefaultAsync(x => x.Id == UserId);

        if (user == null) {
          throw new InvalidOperationException($"User {UserId} does not exist.");
        }

        if (user.Token.IsNullOrWhiteSpace()) {
          throw new InvalidOperationException($"User {UserId} has no token.");
        }

        AccessToken = user.Token;
        Login = user.Login;

        if (user.RateLimitReset != EpochUtility.EpochOffset) {
          UpdateRateLimit(new GitHubRateLimit(
            user.Token,
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

    public static Regex IssueTemplateRegex { get; } = new Regex(
      @"^issue_template(?:\.\w+)?$",
      RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
      TimeSpan.FromMilliseconds(200));

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

    public Task<GitHubResponse<Account>> User(GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest("user", cacheOptions);
      return Fetch<Account>(request);
    }

    public Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest("user/emails", cacheOptions);
      return FetchPaged(request, (UserEmail x) => x.Email);
    }

    public Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}", cacheOptions);
      return Fetch<Repository>(request);
    }

    public Task<GitHubResponse<IEnumerable<Repository>>> Repositories(GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest("user/repos", cacheOptions);
      return FetchPaged(request, (Repository x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, long issueId, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/timeline", cacheOptions) {
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

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{number}", cacheOptions) {
        // https://developer.github.com/v3/issues/#reactions-summary 
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      return Fetch<Issue>(request);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset since, ushort maxPages, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues", cacheOptions) {
        // https://developer.github.com/v3/issues/#reactions-summary 
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      request.AddParameter("state", "all");
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      request.AddParameter("since", since);

      return FetchPaged(request, (Issue x) => x.Id, maxPages);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/reactions", cacheOptions) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments/{commentId}/reactions", cacheOptions) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = null, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/comments", cacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      return FetchPaged(request, (Comment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = null, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments", cacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (Comment x) => x.Id);
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/commits/{hash}", cacheOptions);
      return Fetch<Commit>(request);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/events", cacheOptions);
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (IssueEvent x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/labels", cacheOptions);
      return FetchPaged(request, (Label x) => Tuple.Create(x.Name, x.Color));
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/milestones", cacheOptions);
      request.AddParameter("state", "all");
      return FetchPaged(request, (Milestone x) => x.Id);
    }

    public Task<GitHubResponse<Account>> Organization(string orgName, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"orgs/{orgName}", cacheOptions);
      return Fetch<Account>(request);
    }

    public async Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(string state = "active", GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest("user/memberships/orgs", cacheOptions);
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

    public Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", GitHubCacheDetails cacheOptions = null) {
      // defaults: filter=all, role=all
      var request = new GitHubRequest($"orgs/{orgLogin}/members", cacheOptions);
      request.AddParameter("role", role);
      return FetchPaged(request, (Account x) => x.Id);
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/{pullRequestNumber}", cacheOptions);
      return Fetch<PullRequest>(request);
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/assignees", cacheOptions);
      return FetchPaged(request, (Account x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"orgs/{name}/hooks", cacheOptions);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/hooks", cacheOptions);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"repos/{repoFullName}/hooks", hook);
      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"orgs/{orgName}/hooks", hook);
      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId) {
      var request = new GitHubRequest(HttpMethod.Delete, $"repos/{repoFullName}/hooks/{hookId}");
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId) {
      var request = new GitHubRequest(HttpMethod.Delete, $"orgs/{orgName}/hooks/{hookId}");
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, IEnumerable<string> events) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"repos/{repoFullName}/hooks/{hookId}",
        new {
          Events = events,
        });

      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, IEnumerable<string> events) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"orgs/{orgName}/hooks/{hookId}",
        new {
          Events = events,
        });

      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId) {
      var request = new GitHubRequest(HttpMethod.Post, $"orgs/{name}/hooks/{hookId}/pings");
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId) {
      var request = new GitHubRequest(HttpMethod.Post, $"repos/{repoFullName}/hooks/{hookId}/pings");
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions = null) {
      if (!directoryPath.StartsWith("/", StringComparison.Ordinal)) {
        directoryPath = "/" + directoryPath;
      }
      var request = new GitHubRequest($"repos/{repoFullName}/contents{directoryPath}", cacheOptions);
      return FetchPaged(request, (ContentsFile f) => f.Path);
    }

    public Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions) {
      if (!filePath.StartsWith("/", StringComparison.Ordinal)) {
        filePath = "/" + filePath;
      }
      var request = new GitHubRequest($"repos/{repoFullName}/contents{filePath}", cacheOptions);
      return Fetch<byte[]>(request);
    }

    private Task<GitHubResponse<IEnumerable<Project>>> Projects(string endpoint, GitHubCacheDetails cacheOptions) {
      var request = new GitHubRequest(endpoint, cacheOptions) {
        AcceptHeaderOverride = "application/vnd.github.inertia-preview+json",
      };
      return FetchPaged(request, (Project p) => p.Id);
    }

    public Task<GitHubResponse<IEnumerable<Project>>> RepositoryProjects(string repoFullName, GitHubCacheDetails cacheOptions) {
      return Projects($"repos/{repoFullName}/projects", cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Project>>> OrganizationProjects(string organizationLogin, GitHubCacheDetails cacheOptions) {
      return Projects($"orgs/{organizationLogin}/projects", cacheOptions);
    }

    ////////////////////////////////////////////////////////////
    // Handling
    ////////////////////////////////////////////////////////////

    private SemaphoreSlim _maxConcurrentRequests = new SemaphoreSlim(MaxConcurrentRequests);

    private async Task<GitHubResponse<T>> Fetch<T>(GitHubRequest request) {
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

      // Clear flag since we made it this far.
      _lastRequestLimited = false;

      await _maxConcurrentRequests.WaitAsync();

      GitHubResponse<T> result;
      try {
        result = await SharedHandler.Fetch<T>(this, request);
      } finally {
        _maxConcurrentRequests.Release();
      }

      // Token revocation handling and abuse.
      var response = result as GitHubResponse;
      bool abuse = false;
      DateTimeOffset? limitUntil = null;
      if (response?.Status == HttpStatusCode.Unauthorized) {
        using (var ctx = _shipContextFactory.CreateInstance()) {
          await ctx.RevokeAccessToken(AccessToken);
        }
        DeactivateOnIdle();
      } else if (response.Error?.IsAbuse == true) {
        abuse = true;
        limitUntil = response.RetryAfter ?? DateTimeOffset.UtcNow.AddSeconds(60); // Default to 60 seconds.
      } else if (_rateLimit?.IsExceeded == true) {
        limitUntil = _rateLimit.Reset;
      }

      if (limitUntil != null) {
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
        var changes = new ChangeSummary();
        changes.Users.Add(UserId);
        await _queueClient.NotifyChanges(changes);
      } else if (response.RateLimit != null) {
        // Normal rate limit tracking
        UpdateRateLimit(response.RateLimit);
      }

      return result;
    }

    private async Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubRequest request, Func<T, TKey> keySelector, ushort? maxPages = null) {
      if (request.Method != HttpMethod.Get) {
        throw new InvalidOperationException("Only GETs can be paginated.");
      }

      // Always request the largest page size
      if (!request.Parameters.ContainsKey("per_page")) {
        request.AddParameter("per_page", PageSize);
      }

      // Fetch has the retry logic.
      var result = await Fetch<IEnumerable<T>>(request);

      if (result.IsOk
        && result.Pagination != null
        && (maxPages == null || maxPages > 1)) {
        result = await EnumerateParallel<IEnumerable<T>, T>(result, maxPages);
      }

      return result.Distinct(keySelector);
    }

    private async Task<GitHubResponse<TCollection>> EnumerateParallel<TCollection, TItem>(GitHubResponse<TCollection> firstPage, ushort? maxPages)
      where TCollection : IEnumerable<TItem> {
      var results = new List<TItem>(firstPage.Result);
      IEnumerable<GitHubResponse<TCollection>> batch;
      var partial = false;

      // TODO: Cancellation (for when errors are encountered)?

      if (InterpolationEnabled && firstPage.Pagination?.CanInterpolate == true) {
        var pages = firstPage.Pagination.Interpolate();

        if (maxPages < pages.Count()) {
          partial = true;
          pages = pages.Take((int)maxPages - 1);
        }

        var pageRequestors = pages
          .Select(page => {
            Func<Task<GitHubResponse<TCollection>>> requestor = () => {
              var request = firstPage.Request.CloneWithNewUri(page);
              return Fetch<TCollection>(request);
            };

            return requestor;
          }).ToArray();

        // Check if we can request all the pages within the limit.
        if (firstPage.RateLimit.Remaining < pageRequestors.Length) {
          firstPage.Result = default(TCollection);
          firstPage.Status = HttpStatusCode.Forbidden; // Rate Limited
          return firstPage;
        }

        var accum = new List<GitHubResponse<TCollection>>();
        for (int i = 0; i < pageRequestors.Length;) {
          var tasks = new List<Task<GitHubResponse<TCollection>>>();
          for (int j = 0; j < MaxConcurrentRequests && i < pageRequestors.Length; ++i, ++j) {
            tasks.Add(pageRequestors[i]());
          }
          await Task.WhenAll(tasks);
          foreach (var task in tasks) {
            if (task.IsFaulted) {
              task.Wait(); // force exception to throw
            } else {
              accum.Add(task.Result);
            }
          }
        }
        batch = accum;

        foreach (var response in batch) {
          if (response.IsOk) {
            results.AddRange(response.Result);
          } else if (maxPages != null) {
            // Return results up to this point.
            partial = true;
            break;
          } else {
            return response;
          }
        }
      } else { // Walk in order
        var current = firstPage;
        ushort page = 0;
        while (current.Pagination?.Next != null
          && (maxPages == null || page < maxPages)) {

          var nextReq = current.Request.CloneWithNewUri(current.Pagination.Next);
          current = await Fetch<TCollection>(nextReq);

          if (current.IsOk) {
            results.AddRange(current.Result);
          } else if (maxPages != null) {
            // Return results up to this point.
            partial = true;
            break;
          } else {
            return current;
          }

          ++page;
        }
        // Just use the last request.
        batch = new[] { current };
      }

      // Keep cache and other headers from first page.
      var final = firstPage;
      final.Result = (TCollection)(IEnumerable<TItem>)results;

      // Clear cache data if partial result
      if (partial) {
        final.CacheData = null;
      }

      var rates = batch
        .Select(x => x.RateLimit)
        .GroupBy(x => x.Reset)
        .OrderByDescending(x => x.Key)
        .First();
      final.RateLimit = new GitHubRateLimit(
        final.RateLimit.AccessToken,
        rates.Min(x => x.Limit),
        rates.Min(x => x.Remaining),
        rates.Key);

      return final;
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
