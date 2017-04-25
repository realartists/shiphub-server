namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;

  public class PullRequestCommentEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Issue { get; set; }
    public long Repository { get; set; }
    public long User { get; set; }
    public long? Review { get; set; }
    public string DiffHunk { get; set; }
    public string Path { get; set; }
    public long? Position { get; set; }
    public long? OriginalPosition { get; set; }
    public string CommitId { get; set; }
    public string OriginalCommitId { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
