namespace RealArtists.ShipHub.ActorInterfaces.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using Common.GitHub;
  using Common.GitHub.Models;

  /// <summary>
  /// These GitHub requests and responses are free of user specific state.
  /// </summary>
  public interface IGitHubPoolable {
    Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = default(DateTimeOffset?), GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset since, ushort maxPages, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Account>> Organization(string orgName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Project>>> RepositoryProjects(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Project>>> OrganizationProjects(string organizationLogin, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
  }

  /// <summary>
  /// These operations only make sense for organization administrators. 
  /// </summary>
  public interface IGitHubOrganizationAdmin {
    Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, IEnumerable<string> events, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
  }

  /// <summary>
  /// These operations only make sense for repository administrators. 
  /// </summary>
  public interface IGitHubRepositoryAdmin {
    Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, IEnumerable<string> events, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
  }

  /// <summary>
  /// Interacts with GitHub on behalf of a user, using their credentials.
  /// </summary>
  public interface IGitHubActor : Orleans.IGrainWithIntegerKey, IGitHubPoolable, IGitHubOrganizationAdmin, IGitHubRepositoryAdmin {
    // Implict user scope and permissions (My _)
    Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(string state = "active", GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Repository>>> Repositories(GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Account>> User(GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);

    // Not all users can see the same timeline events
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, long issueId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
  }
}
