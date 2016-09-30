namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class CommentTableType {
    public long Id { get; set; }
    public long? IssueId { get; set; } // Optional
    public int IssueNumber { get; set; } // Required
    public long UserId { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
