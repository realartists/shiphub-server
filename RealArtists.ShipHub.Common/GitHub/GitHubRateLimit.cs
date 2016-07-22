namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  public class GitHubRateLimit {
    public int RateLimit { get; set; }
    public int RateLimitRemaining { get; set; }
    public DateTimeOffset RateLimitReset { get; set; }

    public bool IsOverLimit(int limit) {
      return RateLimitRemaining < limit && RateLimitReset > DateTimeOffset.UtcNow;
    }
  }
}
