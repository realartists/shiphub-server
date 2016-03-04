namespace RealArtists.ShipHub.Api.Configuration {
  using System.Collections.Generic;
  using System.Linq;
  using GitHub;

  public class GitHubApplicationCredentialOptions {
    public string Id { get; set; }
    public string Secret { get; set; }
  }

  public class GitHubOptions {
    public string Name { get; set; }
    public string Version { get; set; }
    public IEnumerable<GitHubApplicationCredentialOptions> Credentials { get; set; }

    public GitHubClient CreateApplicationClient(string applicationId = null) {
      GitHubApplicationCredentialOptions appCreds;
      if (applicationId == null) {
        appCreds = Credentials.Single();
      } else {
        appCreds = Credentials.Single(x => x.Id == applicationId);
      }
      return new GitHubClient(Name, Version, new GitHubApplicationCredentials(appCreds.Id, appCreds.Secret));
    }

    public GitHubClient CreateUserClient(string accessToken) {
      return new GitHubClient(Name, Version, new GitHubOauthCredentials(accessToken));
    }
  }
}
