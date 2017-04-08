namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class IssueTableType {
    // Issue Fields
    public long? Id { get; set; }
    public int Number { get; set; }
    public string State { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public long UserId { get; set; }
    public long? MilestoneId { get; set; }
    public bool Locked { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? ClosedById { get; set; }
    public bool PullRequest { get; set; }
    public string Reactions { get; set; }
  }
}
