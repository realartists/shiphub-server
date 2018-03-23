namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Orleans;
  using Orleans.CodeGeneration;
  using Common.DataModel.Types;

  /// <summary>
  /// Represents a ShipHub user.
  /// </summary>
  [Version(Constants.InterfaceBaseVersion + 3)]
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
    /// Forces a refresh of repos next sync. Helpful when one is known to
    /// have been added or deleted. This also refreshes the sync settings.
    /// </summary>
    Task SyncRepositories();
  }
}
