namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;

  public class CommitCommentEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Repository { get; set; }
    public long User { get; set; }
    public string CommitId { get; set; }
    public string Path { get; set; }
    public long? Line { get; set; }
    public long? Position { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
