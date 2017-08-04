namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Orleans;

  public interface IRepositoryActor : IGrainWithIntegerKey {
    /// <summary>
    /// Right now this simply refreshes a timer. If no sync interest
    /// is observed for a period of time, the grain will deactivate.
    /// 
    /// TODO: Track and return some kind of status
    /// TODO: Publish event streams for sync status and data changes.
    /// </summary>
    Task Sync();

    /// <summary>
    /// Trigger a refresh of the ISSUE_TEMPLATE for the repo.
    /// </summary>
    Task SyncIssueTemplate();

    /// <summary>
    /// This should re-sync all repo contributors and their permisions.
    /// Currently used when a repo is removed.
    /// </summary>
    Task ForceSyncAllLinkedAccountRepositories();

    Task SyncProtectedBranch(string branchName, long forUserId);

    Task RefreshIssueComment(long commentId);

    Task RefreshPullRequestReviewComment(long commentId);

    /// <summary>
    /// Delete and respider all issues and pull requests on the next sync timer tick.
    /// </summary>
    Task ForceResyncRepositoryIssues();
  }
}
