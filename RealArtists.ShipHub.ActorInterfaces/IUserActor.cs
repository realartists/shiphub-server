namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Orleans;

  /// <summary>
  /// Represents a GitHub/ShipHub user.
  /// </summary>
  public interface IUserActor : IGrainWithIntegerKey {
    /// <summary>
    /// Right now this simply refreshes a timer. If no sync interest
    /// is observed for a period of time, the grain will deactivate.
    /// 
    /// TODO: Track and return some kind of status
    /// TODO: Publish event streams for sync status and data changes.
    /// </summary>
    Task Sync();

    /// <summary>
    /// Used by webhooks to force an immediate refresh of repos when
    /// one is known to have been added or deleted.
    /// </summary>
    Task ForceSyncRepositories();
  }
}
