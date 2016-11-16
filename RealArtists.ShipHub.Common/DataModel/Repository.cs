﻿namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;
  using Types;

  public class Repository {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    public long AccountId { get; set; }

    public bool Private { get; set; }

    [Required]
    [StringLength(255)]
    public string Name { get; set; }

    [Required]
    [StringLength(510)]
    public string FullName { get; set; }

    public DateTimeOffset Date { get; set; }

    public string IssueTemplate { get; set; }

    public virtual Account Account { get; set; }

    public string MetadataJson {
      get { return Metadata.SerializeObject(); }
      set { Metadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata Metadata { get; set; }

    public string AssignableMetadataJson {
      get { return AssignableMetadata.SerializeObject(); }
      set { AssignableMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata AssignableMetadata { get; set; }

    public string CommentMetadataJson {
      get { return CommentMetadata.SerializeObject(); }
      set { CommentMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata CommentMetadata { get; set; }

    public string EventMetadataJson {
      get { return EventMetadata.SerializeObject(); }
      set { EventMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata EventMetadata { get; set; }

    public string IssueMetadataJson {
      get { return IssueMetadata.SerializeObject(); }
      set { IssueMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata IssueMetadata { get; set; }

    public string LabelMetadataJson {
      get { return LabelMetadata.SerializeObject(); }
      set { LabelMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata LabelMetadata { get; set; }

    public string MilestoneMetadataJson {
      get { return MilestoneMetadata.SerializeObject(); }
      set { MilestoneMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata MilestoneMetadata { get; set; }

    public string ContentsRootMetadataJson {
      get { return ContentsRootMetadata.SerializeObject(); }
      set { ContentsRootMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata ContentsRootMetadata { get; set; }

    public string ContentsDotGitHubMetadataJson {
      get { return ContentsDotGitHubMetadata.SerializeObject(); }
      set { ContentsDotGitHubMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata ContentsDotGitHubMetadata { get; set; }

    public string ContentsIssueTemplateMetadataJson {
      get { return ContentsIssueTemplateMetadata.SerializeObject(); }
      set { ContentsIssueTemplateMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata ContentsIssueTemplateMetadata { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Comment> Comments { get; set; } = new HashSet<Comment>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<IssueEvent> Events { get; set; } = new HashSet<IssueEvent>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; } = new HashSet<Issue>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Milestone> Milestones { get; set; } = new HashSet<Milestone>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<User> AssignableAccounts { get; set; } = new HashSet<User>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<AccountRepository> LinkedAccounts { get; set; } = new HashSet<AccountRepository>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Label> Labels { get; set; } = new HashSet<Label>();
  }
}
