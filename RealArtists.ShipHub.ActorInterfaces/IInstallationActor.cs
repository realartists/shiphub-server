namespace RealArtists.ShipHub.ActorInterfaces {
  using System;
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using Common.GitHub;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using Orleans.CodeGeneration;

  [Version(Constants.InterfaceBaseVersion + 1)]
  public interface IInstallationActor : IGrainWithIntegerKey {
    Task<GitHubResponse<IDictionary<string, JToken>>> BranchProtection(string repoFullName, string branchName, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background);
    Task UpdateAccessibleRepos();
  }
}
