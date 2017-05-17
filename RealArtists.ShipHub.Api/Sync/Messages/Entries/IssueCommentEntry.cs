namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;

  public class IssueCommentEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Issue { get; set; }
    public long Repository { get; set; }
    public long User { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
