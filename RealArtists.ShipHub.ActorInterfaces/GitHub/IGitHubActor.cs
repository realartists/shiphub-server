namespace RealArtists.ShipHub.ActorInterfaces.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Orleans.CodeGeneration;

  /// <summary>
  /// Interacts with GitHub on behalf of a user, using their credentials.
  /// </summary>
  [Version(1)]
  public interface IGitHubActor : Orleans.IGrainWithIntegerKey, IGitHubPoolable, IGitHubOrganizationAdmin, IGitHubRepositoryAdmin {
    // Implict user scope and permissions (My _)
    Task<GitHubResponse<IEnumerable<Issue>>> IssueMentions(DateTimeOffset? since, uint maxPages, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(string state = "active", GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Repository>>> Repositories(GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Account>> User(GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);

    // Support creating PRs a little more efficiently
    Task<GitHubResponse<PullRequest>> CreatePullRequest(string repoFullName, string title, string body, string baseSha, string headSha, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<Issue>> UpdateIssue(string repoFullName, int number, int? milestone, IEnumerable<string> assignees, IEnumerable<string> labels, RequestPriority priority = RequestPriority.Background);

    // Not all users can see the same timeline events, reviews, and comments
    Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, long issueId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<Review>>> PullRequestReviews(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestReviewComments(string repoFullName, int pullRequestNumber, long pullRequestReviewId, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
  }
}
