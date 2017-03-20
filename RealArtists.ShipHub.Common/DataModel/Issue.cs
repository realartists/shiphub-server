namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;
  using Types;

  public class Issue {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    public long UserId { get; set; }

    public long RepositoryId { get; set; }

    public int Number { get; set; }

    [Required]
    [StringLength(6)]
    public string State { get; set; }

    [Required]
    public string Title { get; set; }

    public string Body { get; set; }

    public long? MilestoneId { get; set; }

    public bool Locked { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public long? ClosedById { get; set; }

    public bool PullRequest { get; set; }

    public string Reactions { get; set; }

    public virtual Account ClosedBy { get; set; }

    public virtual Account User { get; set; }

    public virtual Milestone Milestone { get; set; }

    public virtual Repository Repository { get; set; }

    public string MetadataJson {
      get => Metadata.SerializeObject();
      set => Metadata = value.DeserializeObject<GitHubMetadata>();
    }

    [NotMapped]
    public GitHubMetadata Metadata { get; set; }

    public string CommentMetadataJson {
      get => CommentMetadata.SerializeObject();
      set => CommentMetadata = value.DeserializeObject<GitHubMetadata>();
    }

    [NotMapped]
    public GitHubMetadata CommentMetadata { get; set; }

    public string ReactionMetadataJson {
      get => ReactionMetadata.SerializeObject();
      set => ReactionMetadata = value.DeserializeObject<GitHubMetadata>();
    }

    [NotMapped]
    public GitHubMetadata ReactionMetadata { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Account> Assignees { get; set; } = new HashSet<Account>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Comment> Comments { get; set; } = new HashSet<Comment>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Label> Labels { get; set; } = new HashSet<Label>();
  }
}