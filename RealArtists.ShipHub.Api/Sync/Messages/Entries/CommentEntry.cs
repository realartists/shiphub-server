namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;

  public class CommentEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Issue { get; set; }
    public long Repository { get; set; }
    public long User { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Reactions Reactions { get; set; }
  }
}
