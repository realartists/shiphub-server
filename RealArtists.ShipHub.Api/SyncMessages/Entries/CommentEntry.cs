namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System;

  public class CommentEntry : SyncEntity {
    public long Identifier { get; set; }
    public long IssueIdentifier { get; set; }
    public long RepositoryIdentifier { get; set; }
    public long UserIdentifier { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Reactions Reactions { get; set; }
  }
}
