namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public interface IGitHubResource {
    string ETag { get; set; }
    DateTimeOffset? LastModified { get; set; }
    DateTimeOffset? Expires { get; set; }
    DateTimeOffset LastRefresh { get; set; }
  }

  public abstract class GitHubResource : IGitHubResource, IVersionedResource {
    [StringLength(64)]
    public string ETag { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public DateTimeOffset? Expires { get; set; }

    public DateTimeOffset LastRefresh { get; set; }

    public string ExtensionJson { get; set; }

    // IVersionedResource

    public abstract string TopicName { get; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public long RowVersion { get; set; }

    public long? RestoreVersion { get; set; }
  }
}
