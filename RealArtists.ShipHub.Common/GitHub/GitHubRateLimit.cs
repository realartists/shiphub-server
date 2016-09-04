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

  public static class GitHubRateLimitExtensions {
    public static void ThrowIfUnder(this GitHubRateLimit rateLimit, uint floor, string userInfo) {
      if (rateLimit != null && rateLimit.IsUnder(floor)) {
        throw new GitHubException($"Rate limit exceeded. Only {rateLimit.RateLimitRemaining} requests left before {rateLimit.RateLimitReset:o} ({userInfo}).");
      }
    }
  }
}
