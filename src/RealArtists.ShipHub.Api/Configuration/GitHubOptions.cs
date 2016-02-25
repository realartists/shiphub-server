namespace RealArtists.ShipHub.Api.Configuration {
  using Octokit;

  public class GitHubOptions {
    public string ApplicationId { get; set; }
    public string ApplicationSecret { get; set; }
    public string ApplicationName { get; set; }
    public string ApplicationVersion { get; set; }

    public ProductHeaderValue ProductHeader {
      get {
        return new ProductHeaderValue(ApplicationName, ApplicationVersion);
      }
    }

    public IGitHubClient CreateApplicationClient() {
      return new GitHubClient(ProductHeader) {
        Credentials = new Credentials(ApplicationId, ApplicationSecret)
      };
    }

    public IGitHubClient CreateUserClient(string accessToken) {
      return new GitHubClient(ProductHeader) {
        Credentials = new Credentials(accessToken)
      };
    }
  }
}
