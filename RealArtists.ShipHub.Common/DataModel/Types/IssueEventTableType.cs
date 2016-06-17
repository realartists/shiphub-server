namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class IssueEventTableType {
    public long Id { get; set; }
    public long ActorId { get; set; }
    public string CommitId { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? AssigneeId { get; set; }
    public long? MilestoneId { get; set; }
    public string ExtensionData { get; set; }
  }
}
