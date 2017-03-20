namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  public class GitHubRateLimit {
    public const int RateLimitFloor = 500;

    public string AccessToken { get; }
    public int Limit { get; }
    public int Remaining { get; }
    public DateTimeOffset Reset { get; }

    public bool IsExceeded => Remaining < RateLimitFloor && Reset > DateTimeOffset.UtcNow;

    public GitHubRateLimit(string accessToken, int limit, int remaining, DateTimeOffset reset) {
      AccessToken = accessToken;
      Limit = limit;
      Remaining = remaining;
      Reset = reset;
    }
  }
}
