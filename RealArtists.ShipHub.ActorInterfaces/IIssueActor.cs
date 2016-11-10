namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Orleans;

  /// <summary>
  /// An actor for individual issues within a repository.
  /// Used exclusively for on-demand sync as users interactively view issues.
  /// </summary>
  public interface IIssueActor : IGrainWithIntegerCompoundKey {
    /// <summary>
    /// This refreshes the issue timeline, comments, reactions, etc.
    /// </summary>
    Task SyncInteractive(long forUserId);
  }
}
