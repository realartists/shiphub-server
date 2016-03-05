namespace RealArtists.ShipHub.Api.Utilities {
  using GitHub;
  using DataModel;

  public static class AccessTokenUtility {
    public static void UpdateRateLimits(this GitHubAccessTokenModel token, GitHubResponse response) {
      token.RateLimit = response.RateLimit;
      token.RateLimitRemaining = response.RateLimitRemaining;
      token.RateLimitReset = response.RateLimitReset;
    }
  }
}
