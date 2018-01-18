namespace RealArtists.ShipHub.ActorInterfaces.GitHub {
  using System.Threading.Tasks;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Orleans;
  using Orleans.CodeGeneration;

  [Version(Constants.InterfaceBaseVersion + 1)]
  public interface IGitHubAppActor : IGrainWithIntegerKey {
    Task<GitHubResponse<App>> App(RequestPriority priority = RequestPriority.Background);
    Task<GitHubResponse<TimedToken>> CreateInstallationToken(long installationId, RequestPriority priority = RequestPriority.Background);
  }
}
