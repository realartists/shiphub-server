namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class Comment {
    public int Id { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Account User { get; set; }
    public Reactions Reactions { get; set; }
  }
}
