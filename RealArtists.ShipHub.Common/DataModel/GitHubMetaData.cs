namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;

  [Table("GitHubMetaData")]
  public partial class GitHubMetaData {
    public long Id { get; set; }

    [StringLength(64)]
    public string ETag { get; set; }

    public DateTimeOffset? Expires { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public DateTimeOffset? LastRefresh { get; set; }

    public long? AccessTokenId { get; set; }

    public virtual AccessToken AccessToken { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Account> Accounts { get; set; } = new HashSet<Account>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Comment> Comments { get; set; } = new HashSet<Comment>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; } = new HashSet<Issue>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Milestone> Milestones { get; set; } = new HashSet<Milestone>();
  }
}
