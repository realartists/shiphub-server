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
          AccessToken = user.Token,
          RateLimit = user.RateLimit,
          RateLimitRemaining = user.RateLimitRemaining,
          RateLimitReset = user.RateLimitReset,
        };
      }

      return CreateUserClient(user.Token, rateLimit);
    }

    public static GitHubClient CreateUserClient(string accessToken, GitHubRateLimit rateLimit = null) {
      var client = new GitHubClient(ApplicationName, ApplicationVersion, accessToken, rateLimit);

      // Revoke expired and invalid tokens
      client.Handler = new TokenRevocationHandler(client.Handler, async token => {
        using (var context = new ShipHubContext()) {
          await context.RevokeAccessToken(token);
        }
      });

      return client;
    }
  }
}
