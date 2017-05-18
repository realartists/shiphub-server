namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class CommitComment {
    public long Id { get; set; }
    public string CommitId { get; set; }
    public string Path { get; set; }
    public long? Line { get; set; }
    public long? Position { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Account User { get; set; }
    public ReactionSummary Reactions { get; set; }
  }
}
