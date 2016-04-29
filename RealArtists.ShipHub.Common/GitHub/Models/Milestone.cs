namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class Milestone : GitHubModel {
    public int Id { get; set; }
    public int Number { get; set; }
    public OpenState State { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? DueOn { get; set; }

    public Account Creator { get; set; }
  }
}
