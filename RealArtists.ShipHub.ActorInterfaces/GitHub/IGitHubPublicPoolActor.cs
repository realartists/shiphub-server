namespace RealArtists.ShipHub.ActorInterfaces.GitHub {
  using System;
  using Orleans.CodeGeneration;

  /// <summary>
  /// Interacts with GitHub on behalf of all ship users, using randomly selected credentials.
  /// </summary>
  [Version(Constants.InterfaceBaseVersion + 1)]
  public interface IGitHubPublicPoolActor : Orleans.IGrainWithIntegerKey, IGitHubPoolable {
    // Yep, nothing to do here.
  }
}
