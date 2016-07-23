namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Configuration;
  using System.Reflection;
  using DataModel;
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

    /// <summary>
    /// Creates an anonymous GitHub client. Only useful to complete OAuth due to low rate limit.
    /// </summary>
    public static GitHubClient CreateClient() {
      return new GitHubClient(ApplicationName, ApplicationVersion);
    }

    public static GitHubClient CreateUserClient(User user) {
      if (user == null) {
        throw new ArgumentNullException(nameof(user));
      }

      GitHubRateLimit rateLimit = null;
      if (user.RateLimitReset != EpochUtility.EpochOffset) {
        rateLimit = new GitHubRateLimit() {
          RateLimit = user.RateLimit,
          RateLimitRemaining = rateLimit.RateLimitRemaining,
          RateLimitReset = user.RateLimitReset,
        };
      }

      return new GitHubClient(ApplicationName, ApplicationVersion, GitHubCredentials.ForToken(user.Token), rateLimit);
    }

    public static GitHubClient CreateUserClient(string accessToken, GitHubRateLimit rateLimit = null) {
      return new GitHubClient(ApplicationName, ApplicationVersion, GitHubCredentials.ForToken(accessToken), rateLimit);
    }
  }
}
