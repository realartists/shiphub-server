namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;

  public class CommitStatusTableType {
    public long Id { get; set; }
    public long CreatorId { get; set; }
    public string State { get; set; }
    public string TargetUrl { get; set; }
    public string Description { get; set; }
    public string Context { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
