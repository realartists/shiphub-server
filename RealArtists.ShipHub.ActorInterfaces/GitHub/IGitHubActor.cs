namespace RealArtists.ShipHub.ActorInterfaces.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using System.Threading.Tasks;
  using Common.GitHub;
  using Common.GitHub.Models;

  /// <summary>
  /// These GitHub requests and responses are free of user specific state.
  /// </summary>
  public interface IGitHubPoolable {
    Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<Account>> Organization(string orgName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions = null);
  }

  /// <summary>
  /// These operations only make sense for organization administrators. 
  /// </summary>
  public interface IGitHubOrganizationAdmin {
    Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook);
    Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId);
    Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, string[] events);
    Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId);
    Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, GitHubCacheDetails cacheOptions = null);
  }

  /// <summary>
  /// These operations only make sense for repository administrators. 
  /// </summary>
  public interface IGitHubRepositoryAdmin {
    Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook);
    Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId);
    Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, string[] events);
    Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId);
    Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, GitHubCacheDetails cacheOptions = null);
  }

  /// <summary>
  /// Interacts with GitHub on behalf of a user, using their credentials.
  /// </summary>
  public interface IGitHubActor : Orleans.IGrainWithIntegerKey, IGitHubPoolable, IGitHubOrganizationAdmin, IGitHubRepositoryAdmin {
    [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "Can't have properties on grain interfaces.")]
    Task<GitHubRateLimit> GetLatestRateLimit();

    Task<GitHubResponse<bool>> IsAssignable(string repoFullName, string login);

    // Implict user scope and permissions (My _)
    Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(string state = "active", GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<Repository>>> Repositories(GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<Account>> User(GitHubCacheDetails cacheOptions = null);
    Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(GitHubCacheDetails cacheOptions = null);

    // Not all users can see the same timeline events
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null);
  }
}
