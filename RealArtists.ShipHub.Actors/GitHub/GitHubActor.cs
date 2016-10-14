namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Reflection;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Orleans;
  using Orleans.CodeGeneration;
  using Orleans.Concurrency;
  using dm = Common.DataModel;

  [Reentrant]
  public class GitHubActor : Grain, IGitHubActor, IGrainInvokeInterceptor {
    private IGitHubClient _github;
    private string _accessToken;

    // This is gross
    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    // This is nasty
    private static IGitHubHandler HandlerPipeline { get; } = new PaginationHandler(new GitHubHandler());

    public override async Task OnActivateAsync() {
      _accessToken = this.GetPrimaryKeyString();

      // Load state from DB
      using (var context = new dm.ShipHubContext()) {
        // For now, require user and token to already exist in database.
        var user = await context.Users.SingleAsync(x => x.Token == _accessToken);

        // Ew
        GitHubRateLimit rateLimit = null;
        if (user.RateLimitReset != EpochUtility.EpochOffset) {
          rateLimit = new GitHubRateLimit() {
            AccessToken = user.Token,
            RateLimit = user.RateLimit,
            RateLimitRemaining = user.RateLimitRemaining,
            RateLimitReset = user.RateLimitReset,
          };
        }

        // TODO: Orleans has a concept of state/correlation that we can use
        // instead of this hack or adding parameters to every call.
        _github = new GitHubClient(HandlerPipeline, ApplicationName, ApplicationVersion, $"{user.Id} ({user.Login})", Guid.NewGuid(), user.Token, rateLimit);
      }

      await base.OnActivateAsync();
    }

    public override Task OnDeactivateAsync() {
      _maxConcurrentRequests.Dispose();
      _maxConcurrentRequests = null;
      // Save state to DB

      return base.OnDeactivateAsync();
    }

    ////////////////////////////////////////////////////////////
    // Cute hack to limit concurrency.
    ////////////////////////////////////////////////////////////

    private SemaphoreSlim _maxConcurrentRequests = new SemaphoreSlim(8);

    public async Task<object> Invoke(MethodInfo method, InvokeMethodRequest request, IGrainMethodInvoker invoker) {
      await _maxConcurrentRequests.WaitAsync();
      try {
        return await invoker.Invoke(this, request);
      } finally {
        _maxConcurrentRequests.Release();
      }
    }

    ////////////////////////////////////////////////////////////

    public Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook) {
      return _github.AddOrganizationWebhook(orgName, hook);
    }

    public Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook) {
      return _github.AddRepositoryWebhook(repoFullName, hook);
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      return _github.Assignable(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), IGitHubCacheDetails cacheOptions = null) {
      return _github.Comments(repoFullName, since, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = default(DateTimeOffset?), IGitHubCacheDetails cacheOptions = null) {
      return _github.Comments(repoFullName, issueNumber, since, cacheOptions);
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, IGitHubCacheDetails cacheOptions = null) {
      return _github.Commit(repoFullName, hash, cacheOptions);
    }

    public Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId) {
      return _github.DeleteOrganizationWebhook(orgName, hookId);
    }

    public Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId) {
      return _github.DeleteRepositoryWebhook(repoFullName, hookId);
    }

    public Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, string[] events) {
      return _github.EditOrganizationWebhookEvents(orgName, hookId, events);
    }

    public Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, string[] events) {
      return _github.EditRepositoryWebhookEvents(repoFullName, hookId, events);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      return _github.Events(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<bool>> IsAssignable(string repoFullName, string login) {
      return _github.IsAssignable(repoFullName, login);
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, IGitHubCacheDetails cacheOptions = null) {
      return _github.Issue(repoFullName, number, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, IGitHubCacheDetails cacheOptions = null) {
      return _github.IssueCommentReactions(repoFullName, commentId, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, IGitHubCacheDetails cacheOptions = null) {
      return _github.IssueReactions(repoFullName, issueNumber, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), IGitHubCacheDetails cacheOptions = null) {
      return _github.Issues(repoFullName, since, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      return _github.Labels(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      return _github.Milestones(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", IGitHubCacheDetails cacheOptions = null) {
      return _github.OrganizationMembers(orgLogin, role, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(string state = "active", IGitHubCacheDetails cacheOptions = null) {
      return _github.OrganizationMemberships(state, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, IGitHubCacheDetails cacheOptions = null) {
      return _github.OrganizationWebhooks(name, cacheOptions);
    }

    public Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId) {
      return _github.PingOrganizationWebhook(name, hookId);
    }

    public Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId) {
      return _github.PingRepositoryWebhook(repoFullName, hookId);
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, IGitHubCacheDetails cacheOptions = null) {
      return _github.PullRequest(repoFullName, pullRequestNumber, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Repository>>> Repositories(IGitHubCacheDetails cacheOptions = null) {
      return _github.Repositories(cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      return _github.RepositoryWebhooks(repoFullName, cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, IGitHubCacheDetails cacheOptions = null) {
      return _github.Timeline(repoFullName, issueNumber, cacheOptions);
    }

    public Task<GitHubResponse<Account>> User(IGitHubCacheDetails cacheOptions = null) {
      return _github.User(cacheOptions);
    }

    public Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(IGitHubCacheDetails cacheOptions = null) {
      return _github.UserEmails(cacheOptions);
    }
  }
}
