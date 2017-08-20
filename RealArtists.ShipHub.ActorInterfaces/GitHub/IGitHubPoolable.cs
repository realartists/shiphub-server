namespace RealArtists.ShipHub.ActorInterfaces.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Newtonsoft.Json.Linq;

  /// <summary>
  /// These GitHub requests and responses are free of user specific state.
  /// </summary>
  public interface IGitHubPoolable {
    // Users
    Task<GitHubResponse<Account>> User(string login, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Account>> User(long id, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);

    // Issues
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset since, uint maxPages, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Issue>>> NewestIssues(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);

    Task<GitHubResponse<IssueComment>> IssueComment(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<IssueComment>>> IssueComments(string repoFullName, DateTimeOffset? since = null, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<IssueComment>>> IssueComments(string repoFullName, int issueNumber, DateTimeOffset? since = null, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);

    // Pull Requests
    Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<PullRequest>>> PullRequests(string repoFullName, string sort, string direction, uint skipPages, uint maxPages, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);

    Task<GitHubResponse<PullRequestComment>> PullRequestComment(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestComments(string repoFullName, DateTimeOffset? since = null, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestComments(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Reaction>>> PullRequestCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);

    Task<GitHubResponse<IEnumerable<Review>>> PullRequestReviews(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<PullRequestReviewResult>>> PullRequestReviews(string repoFullName, IEnumerable<int> pullRequestNumbers, RequestPriority priority = RequestPriority.Background);

    // Repos
    Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<IssueComment>>> Comments(string repoFullName, DateTimeOffset since, uint maxPages, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<CommitComment>>> CommitComments(string repoFullName, string reference, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Reaction>>> CommitCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<CommitStatus>>> CommitStatuses(string repoFullName, string reference, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IDictionary<string, JToken>>> BranchProtection(string repoFullName, string branchName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Repository>> Repository(long repoId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Project>>> RepositoryProjects(string repoFullName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);

    Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);

    // Orgs
    Task<GitHubResponse<Account>> Organization(string login, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Account>> Organization(long id, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Project>>> OrganizationProjects(string organizationLogin, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
  }

  public class PullRequestReviewResult {
    public long PullRequestId { get; set; }
    public IEnumerable<Review> Reviews { get; set; }
    public bool MoreResults { get; set; }
  }
}
