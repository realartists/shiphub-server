namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Web;

  public class MilestoneEntry {
    public long Identifier { get; set; }
    public string MilestoneDescription { get; set; }
    public string Name { get; set; }
    public string State { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DueOn { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}