namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class Repository {
    public int Id { get; set; }
    public Account Owner { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
    public bool Private { get; set; }
    public bool HasIssues { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
