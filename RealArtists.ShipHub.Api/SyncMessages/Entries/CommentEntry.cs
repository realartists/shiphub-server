namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Web;

  public class CommentEntry {
    public long Identifier { get; set; }
    public long Issue { get; set; }
    public long Repository { get; set; }
    public long User { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Reactions MyProperty { get; set; }
  }
}