namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class App {
    public DateTimeOffset CreatedAt { get; set; }
    public string Description { get; set; }
    public long Id { get; set; }
    public string Name { get; set; }
    public Account Owner { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
