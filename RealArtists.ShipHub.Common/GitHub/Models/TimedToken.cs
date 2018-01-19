namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class TimedToken {
    public string Token { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
  }
}
