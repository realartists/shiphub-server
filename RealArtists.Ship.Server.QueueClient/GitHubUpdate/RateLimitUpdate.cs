namespace RealArtists.Ship.Server.QueueClient.GitHubUpdate {
  using System;

  public class RateLimitUpdate {
    public string AccessToken { get; set; }
    public int RateLimit { get; set; }
    public int RateLimitRemaining { get; set; }
    public DateTimeOffset RateLimitReset { get; set; }
  }
}
