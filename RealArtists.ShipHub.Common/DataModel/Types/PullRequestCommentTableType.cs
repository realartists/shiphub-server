namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class PullRequestCommentTableType {
    public long Id { get; set; }
    public long UserId { get; set; }
    public long PullRequestReviewId { get; set; }
    public string DiffHunk { get; set; }
    public string Path { get; set; }
    public long? Position { get; set; }
    public long? OriginalPosition { get; set; }
    public string CommitId { get; set; }
    public string OriginalCommitId { get; set; }
    public long? InReplyTo { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
