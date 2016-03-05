namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;

  public abstract class GitHubApiResource {
    [StringLength(64)]
    public string ETag { get; set; }

    public DateTimeOffset? LastModified { get; set; }
    
    public DateTimeOffset? ValidUntil { get; set; }

    public DateTimeOffset LastRefresh { get; set; }
  }
}
