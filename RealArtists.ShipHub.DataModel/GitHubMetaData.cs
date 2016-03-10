namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public interface IGitHubResource {
    GitHubMetaData GitHubMetaData { get; set; }
  }

  /// <summary>
  /// Common tracking data used to minimize GitHub API requests.
  /// </summary>
  [ComplexType]
  public class GitHubMetaData {
    [StringLength(64)]
    public string ETag { get; set; }

    public DateTimeOffset? LastModified { get; set; }
    
    public DateTimeOffset? Expires { get; set; }

    public DateTimeOffset LastRefresh { get; set; }
  }
}
