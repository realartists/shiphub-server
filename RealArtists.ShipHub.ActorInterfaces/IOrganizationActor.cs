namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Orleans;
  using Orleans.CodeGeneration;

  /// <summary>
  /// Represents a Github/ShipHub organization.
  /// </summary>
  [Version(1)]
  public interface IOrganizationActor : IGrainWithIntegerKey {
    /// <summary>
    /// Right now this simply refreshes a timer. If no sync interest
    /// is observed for a period of time, the grain will deactivate.
    /// 
    /// TODO: Track and return some kind of status
    /// TODO: Publish event streams for sync status and data changes.
    /// </summary>
    Task Sync();

    /// <summary>
    /// This should re-sync all members and their permisions.
    /// Currently used when a repo is removed or added.
    /// </summary>
    Task ForceSyncAllMemberRepositories();
  }
}
