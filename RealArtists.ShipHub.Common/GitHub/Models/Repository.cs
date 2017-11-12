namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;

  public class RepositoryPermissions {
    public bool Admin { get; set; }
    public bool Push { get; set; }
    public bool Pull { get; set; }
  }

  public class Repository {
    public long Id { get; set; }
    public Account Owner { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
    public bool Private { get; set; }
    public bool HasIssues { get; set; }
    public bool HasProjects { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public RepositoryPermissions Permissions { get; set; }
    public string DefaultBranch { get; set; }
    public long Size { get; set; }
    public bool Archived { get; set; }
  }
}
