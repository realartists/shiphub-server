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
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Orleans;
  using Orleans.CodeGeneration;
  using Orleans.Concurrency;
  using QueueClient;
  using dm = Common.DataModel;

  [Reentrant]
  public class GitHubActor : Grain, IGitHubActor, IGrainInvokeInterceptor, IDisposable {
    public const int MaxConcurrentRequests = 4;

    private IFactory<dm.ShipHubContext> _shipContextFactory;
    private IShipHubQueueClient _queueClient;
    private IShipHubConfiguration _configuration;


    private long _userId;
    private string _login;
    private GitHubClient _github;
    private DateTimeOffset? _abuseDelay;

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

    public GitHubActor(IFactory<dm.ShipHubContext> shipContextFactory, IShipHubQueueClient queueClient, IShipHubConfiguration configuration) {
      _shipContextFactory = shipContextFactory;
      _queueClient = queueClient;
      _configuration = configuration;
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

        _login = user.Login;

        GitHubRateLimit rateLimit = null;
        if (user.RateLimitReset != EpochUtility.EpochOffset) {
          rateLimit = new GitHubRateLimit(
            user.Token,
            user.RateLimit,
            user.RateLimitRemaining,
            user.RateLimitReset);
        }

        var handler = GetOrCreateHandlerPipeline(_shipContextFactory);
        // TODO: Orleans has a concept of state/correlation that we can use
        // instead of Guid.NewGuid() or adding parameters to every call.
        _github = new GitHubClient(_configuration.GitHubApiRoot, handler, ApplicationName, ApplicationVersion, $"{user.Id} ({user.Login})", Guid.NewGuid(), user.Id, user.Token, rateLimit);
      }

      await base.OnActivateAsync();
    }

    public override async Task OnDeactivateAsync() {
      using (var context = _shipContextFactory.CreateInstance()) {
        await context.UpdateRateLimit(_github.RateLimit);
      }

      Dispose();

      await base.OnDeactivateAsync();
    }

    ////////////////////////////////////////////////////////////
    // All per request limiting and checking
    ////////////////////////////////////////////////////////////

    private SemaphoreSlim _maxConcurrentRequests = new SemaphoreSlim(MaxConcurrentRequests);

    public async Task<object> Invoke(MethodInfo method, InvokeMethodRequest request, IGrainMethodInvoker invoker) {
      if (_abuseDelay != null && _abuseDelay.Value > DateTimeOffset.UtcNow) {
        throw new GitHubRateException($"{_login} ({_userId})", null, _github.RateLimit, true);
      }

      await _maxConcurrentRequests.WaitAsync();

      object result;
      try {
        result = await invoker.Invoke(this, request);
      } finally {
        _maxConcurrentRequests.Release();
      }

      DateTimeOffset? limitUntil = null;
      var response = result as GitHubResponse;

      // Token revocation handling and abuse.
      if (response?.Status == HttpStatusCode.Unauthorized) {
        using (var ctx = _shipContextFactory.CreateInstance()) {
          await ctx.RevokeAccessToken(_github.AccessToken);
        }
        DeactivateOnIdle();
      } else if (response.Error?.IsAbuse == true) {
        var retryAfter = response.RetryAfter ?? DateTimeOffset.UtcNow.AddSeconds(60); // Default to 60 seconds.

        if (_abuseDelay == null || _abuseDelay < retryAfter) {
          _abuseDelay = retryAfter;
        }
        limitUntil = _abuseDelay;
      } else if (_github.RateLimit?.IsExceeded == true) {
        limitUntil = _github.RateLimit.Reset;
      }

      if (limitUntil != null) {
        using (var context = _shipContextFactory.CreateInstance()) {
          var oldRate = _github.RateLimit;
          var newRate = new GitHubRateLimit(
            oldRate.AccessToken,
            oldRate.Limit,
            Math.Min(oldRate.Remaining, GitHubRateLimit.RateLimitFloor - 1),
            limitUntil.Value);
          _github.UpdateInternalRateLimit(newRate);
          await context.UpdateRateLimit(newRate);
        }

        var changes = new ChangeSummary();
        changes.Users.Add(_userId);
        await _queueClient.NotifyChanges(changes);
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

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions = null) {
      return _github.Issue(repoFullName, number, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null) {
      return _github.IssueCommentReactions(repoFullName, commentId, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null) {
      return _github.IssueReactions(repoFullName, issueNumber, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset since, ushort maxPages, GitHubCacheDetails cacheOptions = null) {
      return _github.Issues(repoFullName, since, maxPages, cacheOptions);
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

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, long issueId, GitHubCacheDetails cacheOptions = null) {
      return _github.Timeline(repoFullName, issueNumber, issueId, cacheOptions);
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

    public Task<GitHubResponse<IEnumerable<Project>>> RepositoryProjects(string repoFullName, GitHubCacheDetails cacheOptions = null) {
      return _github.RepositoryProjects(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Project>>> OrganizationProjects(string organizationLogin, GitHubCacheDetails cacheOptions = null) {
      return _github.OrganizationProjects(organizationLogin, cacheOptions);
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
