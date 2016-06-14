namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Web;

  public class EventEntry {
    public long Identifier { get; set; }
    public string CommitId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Event { get; set; }
  }
}