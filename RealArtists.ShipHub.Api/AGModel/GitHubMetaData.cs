namespace RealArtists.ShipHub.Api.AGModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;

  [Table("GitHubMetaData")]
  public partial class GitHubMetaData {
    [SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
    public GitHubMetaData() {
      Accounts = new HashSet<Account>();
      Comments = new HashSet<Comment>();
      Issues = new HashSet<Issue>();
      Milestones = new HashSet<Milestone>();
    }

    public long Id { get; set; }

    [StringLength(64)]
    public string ETag { get; set; }

    public DateTimeOffset? Expires { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public DateTimeOffset? LastRefresh { get; set; }

    public long? AccessTokenId { get; set; }

    public virtual AccessToken AccessToken { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Account> Accounts { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Comment> Comments { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Milestone> Milestones { get; set; }
  }
}
