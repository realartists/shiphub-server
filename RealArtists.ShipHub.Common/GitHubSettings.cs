namespace RealArtists.ShipHub.Common {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.Reflection;
  using DataModel;
  using GitHub;

  public static class GitHubSettings {
    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters", Justification = "Only a valid operation for users.")]
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
