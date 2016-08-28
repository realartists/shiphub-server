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

    Task<GitHubResponse<Webhook>> AddOrgWebhook(string orgName, Webhook hook);
    Task<GitHubResponse<Webhook>> AddRepoWebhook(string repoFullName, Webhook hook);
    Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = default(DateTimeOffset?), IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<bool>> DeleteOrgWebhook(string orgName, long hookId);
    Task<GitHubResponse<bool>> DeleteRepoWebhook(string repoFullName, long hookId);
    Task<GitHubResponse<Webhook>> EditOrgWebhookEvents(string orgName, long hookId, string[] events);
    Task<GitHubResponse<Webhook>> EditRepoWebhookEvents(string repoFullName, long hookId, string[] events);
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<bool>> IsAssignable(string repoFullName, string login, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Account>>> Organizations(IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Webhook>>> OrgWebhooks(string name, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<bool>> PingRepoWebhook(string repoFullname, long hookId);
    Task<GitHubResponse<bool>> PingOrgWebhook(string name, long hookId);
    Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Repository>>> Repositories(IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Webhook>>> RepoWebhooks(string repoFullName, IGitHubCacheMetadata cacheOptions = null);
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, IGitHubCacheMetadata cacheOptions = null);
    void UpdateInternalRateLimit(GitHubRateLimit rateLimit);
    Task<GitHubResponse<Account>> User(IGitHubCacheMetadata cacheOptions = null);
  }
}
