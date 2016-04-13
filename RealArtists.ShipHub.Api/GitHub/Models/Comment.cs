namespace RealArtists.ShipHub.Api.GitHub.Models {
  using System;

  public class Comment : GitHubModel {
    public int Id { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Account User { get; set; }
  }
}
