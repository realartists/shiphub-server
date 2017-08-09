namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Orleans;
  using Orleans.CodeGeneration;
  using Common.DataModel.Types;

  /// <summary>
  /// Represents a ShipHub user.
  /// </summary>
  [Version(1)]
  public interface IUserActor : IGrainWithIntegerKey {
    /// <summary>
    /// Saves the provided sync settings and applies them immediately.
    /// </summary>
    /// <param name="settings">The new sync settings.</param>
    Task SetSyncSettings(SyncSettings syncSettings);

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

    /// <summary>
    /// Actually performs the sync operation. Because the UserActor needs
    /// to respond immediately to settings and repo changes, we can't 
    /// just set flags and wait a sync cycle. To avoid races, we have the
    /// sync timer actually make a grain call to this method and leverage
    /// the single threaded grain scheduler.
    /// </summary>
    Task InternalSync();
  }
}
