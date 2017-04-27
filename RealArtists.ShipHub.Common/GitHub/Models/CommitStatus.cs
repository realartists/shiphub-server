namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class CommitStatus {
    public long Id { get; set; }
    public Account Creator { get; set; }
    public string State { get; set; }
    public string TargetUrl { get; set; }
    public string Description { get; set; }
    public string Context { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
