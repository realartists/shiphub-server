namespace RealArtists.ShipHub.Common.GitHub.Models.GraphQL {
  using System;

  public class RateLimit {
    public int Cost { get; set; }
    public int NodeCount { get; set; }
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public DateTimeOffset ResetAt { get; set; }
  }
}
