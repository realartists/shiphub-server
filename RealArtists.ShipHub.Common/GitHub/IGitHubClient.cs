namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Net.Http.Headers;
  using System.Threading.Tasks;
  using Models;

  public interface IGitHubClient {
    Uri ApiRoot { get; }
    string DefaultToken { get; set; }
    IGitHubHandler Handler { get; set; }
    GitHubRateLimit RateLimit { get; }
    ProductInfoHeaderValue UserAgent { get; }
    string UserInfo { get; }
    Guid CorrelationId { get; }

    Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook);
    Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook);
    Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = default(DateTimeOffset?), IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId);
    Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId);
    Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, string[] events);
    Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, string[] events);
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<bool>> IsAssignable(string repoFullName, string login);
    Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId);
    Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId);
    Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Repository>>> Repositories(IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, IGitHubCacheDetails cacheOptions = null);
    void UpdateInternalRateLimit(GitHubRateLimit rateLimit);
    Task<GitHubResponse<Account>> User(IGitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(IGitHubCacheDetails cacheOptions = null);
    int NextRequestId();
  }
}
