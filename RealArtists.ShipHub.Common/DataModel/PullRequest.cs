namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations.Schema;
  using Types;
  using g = GitHub.Models;

  public class PullRequest {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }
    public long IssueId { get; set; }
    public long RepositoryId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string MergeCommitSha { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public int? Additions { get; set; }
    public int? ChangedFiles { get; set; }
    public int? Commits { get; set; }
    public int? Deletions { get; set; }
    public bool? MaintainerCanModify { get; set; }
    public bool? Mergeable { get; set; }
    public string MergeableState { get; set; }
    public long? MergedById { get; set; }
    public bool? Rebaseable { get; set; }

    public virtual Issue Issue { get; set; }
    public virtual Repository Repository { get; set; }

    // Head
    [NotMapped]
    public g.CommitReference Head { get; set; }

    public string HeadJson {
      get => Head.SerializeObject();
      set => Head = value.DeserializeObject<g.CommitReference>();
    }

    // Base
    [NotMapped]
    public g.CommitReference Base { get; set; }

    public string BaseJson {
      get => Base.SerializeObject();
      set => Base = value.DeserializeObject<g.CommitReference>();
    }

    // Metadata
    [NotMapped]
    public GitHubMetadata Metadata { get; set; }

    public string MetadataJson {
      get => Metadata.SerializeObject();
      set => Metadata = value.DeserializeObject<GitHubMetadata>();
    }

    // CommentMetadata
    [NotMapped]
    public GitHubMetadata CommentMetadata { get; set; }

    public string CommentMetadataJson {
      get => CommentMetadata.SerializeObject();
      set => CommentMetadata = value.DeserializeObject<GitHubMetadata>();
    }

    // StatusMetadata
    [NotMapped]
    public GitHubMetadata StatusMetadata { get; set; }

    public string StatusMetadataJson {
      get => StatusMetadata.SerializeObject();
      set => StatusMetadata = value.DeserializeObject<GitHubMetadata>();
    }
  }
}
