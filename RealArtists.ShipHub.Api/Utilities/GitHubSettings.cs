namespace RealArtists.ShipHub.Api.Utilities {
  using System.Collections.Generic;
  using System.Configuration;
  using System.Reflection;
  using GitHub;

  public static class GitHubSettings {
    public const string GitHubSettingsKey = "GitHubCredentials";
    public static readonly string ApplicationName;
    public static readonly string ApplicationVersion;
    public static readonly IReadOnlyDictionary<string, string> Credentials;

    static GitHubSettings() {
      var name = Assembly.GetExecutingAssembly().GetName();
      ApplicationName = name.Name;
      ApplicationVersion = name.Version.ToString();

      var creds = new Dictionary<string, string>();
      var credSetting = ConfigurationManager.AppSettings[GitHubSettingsKey] ?? "";
      foreach (var app in credSetting.Split(';')) {
        var parts = app.Split(':');
        creds.Add(parts[0], parts[1]);
      }
      Credentials = creds;
    }

    public static GitHubClient CreateClient() {
      return new GitHubClient(ApplicationName, ApplicationVersion);
    }

    public static GitHubClient CreateUserClient(string accessToken) {
      return new GitHubClient(ApplicationName, ApplicationVersion, new GitHubOauthCredentials(accessToken));
    }

    public static GitHubClient CreateApplicationClient(string applicationId) {
      if (!Credentials.ContainsKey(applicationId)) {
        return null;
      }

      var secret = Credentials[applicationId];
      return new GitHubClient(ApplicationName, ApplicationVersion, new GitHubApplicationCredentials(applicationId, secret));
    }
  }
}