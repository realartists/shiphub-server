namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public interface IGitHubResource {
    string ETag { get; set; }
    DateTimeOffset? Expires { get; set; }
    DateTimeOffset? LastModified { get; set; }
    DateTimeOffset LastRefresh { get; set; }

    long CacheTokenId { get; set; }
    AccessToken CacheToken { get; set; }

    string ExtensionJson { get; set; }
  }

  public abstract class GitHubResource : IGitHubResource, IVersionedResource {
    

    // Just in case

    public string ExtensionJson { get; set; }

    // Version Data

    public abstract string TopicName { get; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public long RowVersion { get; set; }

    public long? RestoreVersion { get; set; }
  }
}
