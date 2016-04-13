namespace RealArtists.ShipHub.Api {
  using GitHub;
  using DataModel;

  public static class AccessTokenUtility {
    // This isn't a mapping because it's important it be obvious and explicit.
    public static void UpdateRateLimits(this AccessToken token, GitHubResponse response) {
      token.RateLimit = response.RateLimit;
      token.RateLimitRemaining = response.RateLimitRemaining;
      token.RateLimitReset = response.RateLimitReset;
    }
  }
}
