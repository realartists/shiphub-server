namespace RealArtists.ShipHub.Api.Models {
  using System;

  public class ApiMilestone {
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DueOn { get; set; }
    public int Identifier { get; set; }
    public string MilestoneDescription { get; set; }
    public string Name { get; set; }
    public string State { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}