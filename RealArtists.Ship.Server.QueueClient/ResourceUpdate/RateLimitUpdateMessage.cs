namespace RealArtists.Ship.Server.QueueClient.ResourceUpdate {
  using System;

  public class RateLimitUpdateMessage {
    public string AccessToken { get; set; }
    public int RateLimit { get; set; }
    public int RateLimitRemaining { get; set; }
    public DateTimeOffset RateLimitReset { get; set; }
  }
}
