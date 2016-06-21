namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System;

  public class MilestoneEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Repository { get; set; }
    public int Number { get; set; }
    public string State { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DueOn { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
  }
}
