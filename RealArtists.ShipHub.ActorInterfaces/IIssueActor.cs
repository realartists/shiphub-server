namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Common.GitHub;
  using Orleans;
  using Orleans.CodeGeneration;

  /// <summary>
  /// An actor for individual issues within a repository.
  /// Used exclusively for on-demand sync as users interactively view issues.
  /// </summary>
  [Version(1)]
  public interface IIssueActor : IGrainWithIntegerCompoundKey {
    /// <summary>
    /// This refreshes the issue timeline, comments, reactions, etc.
    /// </summary>
    Task SyncTimeline(long? forUserId = null, RequestPriority priority = RequestPriority.Background);
  }
}
