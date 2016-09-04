namespace RealArtists.ShipHub.Common {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.Reflection;
  using DataModel;
  using GitHub;

  public static class GitHubSettings {
    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private static IGitHubHandler HandlerPipeline { get; } = CreatePipeline();

    private static IGitHubHandler CreatePipeline() {
      IGitHubHandler handler = new GitHubHandler();
      handler = new ShipHubFilter(handler);
      handler = new PaginationHandler(handler);

      // Revoke expired and invalid tokens
      handler = new TokenRevocationHandler(handler, async token => {
        using (var context = new ShipHubContext()) {
          await context.RevokeAccessToken(token);
        }
      });

      return handler;
    }

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

      return CreateUserClient(user.Token, $"{user.Login}/{user.Id}", rateLimit);
    }

    public static GitHubClient CreateUserClient(string accessToken, string userInfo, GitHubRateLimit rateLimit = null) {
      return new GitHubClient(HandlerPipeline, ApplicationName, ApplicationVersion, userInfo, accessToken, rateLimit);
    }
  }
}
