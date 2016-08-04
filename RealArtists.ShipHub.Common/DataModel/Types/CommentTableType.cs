namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class CommentTableType {
    public long Id { get; set; }
    public int IssueNumber { get; set; }
    public long UserId { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
