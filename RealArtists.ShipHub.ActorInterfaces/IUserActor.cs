namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Orleans;
  using Orleans.CodeGeneration;

  /// <summary>
  /// Represents a ShipHub user.
  /// </summary>
  [Version(1)]
  public interface IUserActor : IGrainWithIntegerKey {
    /// <summary>
    /// Indicates sync interest. If none is observed for a period of time,
    /// the grain will deactivate.
    /// </summary>
    Task Sync();

    /// <summary>
    /// Called whenever a client connects and says Hello.
    /// </summary>
    Task SyncBillingState();

    /// <summary>
    /// Forces a refresh of repos next sync. Helpful when one is known to
    /// have been added or deleted. This also refreshes the sync settings.
    /// </summary>
    Task SyncRepositories();
  }
}
