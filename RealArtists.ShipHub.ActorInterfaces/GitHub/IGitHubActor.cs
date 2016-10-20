namespace RealArtists.ShipHub.ActorInterfaces.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using Common.GitHub;
  using Common.GitHub.Models;

  public interface IGitHubActor : Orleans.IGrainWithStringKey {
    Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook);
    Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook);
    Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId);
    Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId);
    Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, string[] events);
    Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, string[] events);
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<bool>> IsAssignable(string repoFullName, string login);
    Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<Account>> Organization(string orgName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(string state = "active", GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId);
    Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId);
    Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Repository>>> Repositories(GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<Account>> User(GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(GitHubCacheDetails cacheOptions = null);
  }
}
