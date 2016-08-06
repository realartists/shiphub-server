namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class Reaction {
    public long Id { get; set; }
    public Account User { get; set; }
    public string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
  }
}
