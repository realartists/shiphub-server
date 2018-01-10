namespace RealArtists.ShipHub.ActorInterfaces {
  using System.Threading.Tasks;
  using Orleans;
  using Orleans.CodeGeneration;

  /// <summary>
  /// This is just for James.
  /// </summary>
  [Version(Constants.InterfaceBaseVersion + 1)]
  public interface IMentionsActor : IGrainWithIntegerKey {
    /// <summary>
    /// Indicates sync interest. If none is observed for a period of time,
    /// the grain will deactivate.
    /// </summary>
    Task Sync();
  }
}
