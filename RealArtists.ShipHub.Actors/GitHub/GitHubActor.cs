namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Net;
  using System.Reflection;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Orleans;
  using Orleans.CodeGeneration;
  using Orleans.Concurrency;
  using dm = Common.DataModel;

  [Reentrant]
  public class GitHubActor : Grain, IGitHubActor, IGrainInvokeInterceptor, IDisposable {
    private IFactory<dm.ShipHubContext> _shipContextFactory;

    private long _userId;
    private GitHubClient _github;
    private DateTimeOffset? _retryAfter;

    // TODO: Overhaul this code and trim/eliminate the pipeline
    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private static IGitHubHandler _handler;
    private static IGitHubHandler GetOrCreateHandlerPipeline(IFactory<dm.ShipHubContext> shipContextFactory) {
      if (_handler != null) {
        return _handler;
      }

      IGitHubHandler handler = new GitHubHandler();
      handler = new SneakyCacheFilter(handler, shipContextFactory);
      handler = new PaginationHandler(handler);

      return (_handler = handler);
    }

    public GitHubActor(IFactory<dm.ShipHubContext> shipContextFactory) {
      _shipContextFactory = shipContextFactory;
    }

    public override async Task OnActivateAsync() {
      _userId = this.GetPrimaryKeyLong();

      using (var context = _shipContextFactory.CreateInstance()) {
        // Require user and token to already exist in database.
        var user = await context.Users.SingleOrDefaultAsync(x => x.Id == _userId);

        if (user == null) {
          throw new InvalidOperationException($"User {_userId} does not exist.");
        }

        if (user.Token.IsNullOrWhiteSpace()) {
          throw new InvalidOperationException($"User {_userId} has no token.");
        }

        GitHubRateLimit rateLimit = null;
        if (user.RateLimitReset != EpochUtility.EpochOffset) {
          rateLimit = new GitHubRateLimit() {
            AccessToken = user.Token,
            RateLimit = user.RateLimit,
            RateLimitRemaining = user.RateLimitRemaining,
            RateLimitReset = user.RateLimitReset,
          };
        }

        var handler = GetOrCreateHandlerPipeline(_shipContextFactory);
        // TODO: Orleans has a concept of state/correlation that we can use
        // instead of Guid.NewGuid() or adding parameters to every call.
        _github = new GitHubClient(handler, ApplicationName, ApplicationVersion, $"{user.Id} ({user.Login})", Guid.NewGuid(), user.Id, user.Token, rateLimit);
      }

      await base.OnActivateAsync();
    }

    public override async Task OnDeactivateAsync() {
      // Save state
      using (var context = _shipContextFactory.CreateInstance()) {
        await context.UpdateRateLimit(_github.RateLimit);
      }

      Dispose();

      await base.OnDeactivateAsync();
    }

    ////////////////////////////////////////////////////////////
    // All per request limiting and checking
    // * Limit concurrency
    // * Clear invalid tokens
    // * More?
    ////////////////////////////////////////////////////////////

    private SemaphoreSlim _maxConcurrentRequests = new SemaphoreSlim(4);

    public async Task<object> Invoke(MethodInfo method, InvokeMethodRequest request, IGrainMethodInvoker invoker) {
      if (_retryAfter != null && _retryAfter.Value > DateTimeOffset.UtcNow) {
        throw new GitHubException($"Abuse reported. Dropping requests until {_retryAfter}");
      }

      await _maxConcurrentRequests.WaitAsync();

      object result;
      try {
        result = await invoker.Invoke(this, request);
      } finally {
        _maxConcurrentRequests.Release();
      }

      var response = result as GitHubResponse;

      // Abuse detection and throttling.
      if (response.Error?.IsAbuse == true) {
        var retryAfter = response.RetryAfter ?? DateTimeOffset.UtcNow.AddSeconds(60); // Default to 60 seconds.

        if (_retryAfter == null || _retryAfter < retryAfter) {
          _retryAfter = retryAfter;
        }
      }

      // Token revocation handling.
      if (response?.Status == HttpStatusCode.Unauthorized) {
        using (var ctx = _shipContextFactory.CreateInstance()) {
          await ctx.RevokeAccessToken(_github.AccessToken);
        }
        DeactivateOnIdle();
      }

      return result;
    }

    ////////////////////////////////////////////////////////////

    public Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook) {
      return _github.AddOrganizationWebhook(orgName, hook);
    }

    public Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook) {
      return _github.AddRepositoryWebhook(repoFullName, hook);
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return _github.Assignable(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null) {
      return _github.Comments(repoFullName, since, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null) {
      return _github.Comments(repoFullName, issueNumber, since, cacheOptions);
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions = null) {
      return _github.Commit(repoFullName, hash, cacheOptions);
    }

    public Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId) {
      return _github.DeleteOrganizationWebhook(orgName, hookId);
    }

    public Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId) {
      return _github.DeleteRepositoryWebhook(repoFullName, hookId);
    }

    public Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, IEnumerable<string> events) {
      return _github.EditOrganizationWebhookEvents(orgName, hookId, events);
    }

    public Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, IEnumerable<string> events) {
      return _github.EditRepositoryWebhookEvents(repoFullName, hookId, events);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return _github.Events(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<bool>> IsAssignable(string repoFullName, string login) {
      return _github.IsAssignable(repoFullName, login);
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions = null) {
      return _github.Issue(repoFullName, number, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null) {
      return _github.IssueCommentReactions(repoFullName, commentId, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null) {
      return _github.IssueReactions(repoFullName, issueNumber, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null) {
      return _github.Issues(repoFullName, since, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return _github.Labels(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return _github.Milestones(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<Account>> Organization(string orgName, GitHubCacheDetails cacheOptions = null) {
      return _github.Organization(orgName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", GitHubCacheDetails cacheOptions = null) {
      return _github.OrganizationMembers(orgLogin, role, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(string state = "active", GitHubCacheDetails cacheOptions = null) {
      return _github.OrganizationMemberships(state, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, GitHubCacheDetails cacheOptions = null) {
      return _github.OrganizationWebhooks(name, cacheOptions);
    }

    public Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId) {
      return _github.PingOrganizationWebhook(name, hookId);
    }

    public Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId) {
      return _github.PingRepositoryWebhook(repoFullName, hookId);
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null) {
      return _github.PullRequest(repoFullName, pullRequestNumber, cacheOptions);
    }

    public Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return _github.Repository(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Repository>>> Repositories(GitHubCacheDetails cacheOptions = null) {
      return _github.Repositories(cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return _github.RepositoryWebhooks(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null) {
      return _github.Timeline(repoFullName, issueNumber, cacheOptions);
    }

    public Task<GitHubResponse<Account>> User(GitHubCacheDetails cacheOptions = null) {
      return _github.User(cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(GitHubCacheDetails cacheOptions = null) {
      return _github.UserEmails(cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions = null) {
      return _github.ListDirectoryContents(repoFullName, directoryPath, cacheOptions);
    }

    public Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions = null) {
      return _github.FileContents(repoFullName, filePath, cacheOptions);
    }

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
