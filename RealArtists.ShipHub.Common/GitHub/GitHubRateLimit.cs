namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  public class GitHubRateLimit {
    public string AccessToken { get; set; }
    public int RateLimit { get; set; }
    public int RateLimitRemaining { get; set; }
    public DateTimeOffset RateLimitReset { get; set; }

    public bool IsUnder(uint floor) {
      return RateLimitRemaining < floor && RateLimitReset > DateTimeOffset.UtcNow;
    }
  }
}
