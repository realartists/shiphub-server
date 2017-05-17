namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class CommitCommentTableType {
    public long Id { get; set; }
    public long UserId { get; set; }
    public string CommitId { get; set; }
    public string Path { get; set; }
    public long? Line { get; set; }
    public long? Position { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
