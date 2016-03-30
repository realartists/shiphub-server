namespace RealArtists.ShipHub.Api.AGModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;

  public partial class Account {
    [SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
    public Account() {
      AccessTokens = new HashSet<AccessToken>();
      Comments = new HashSet<Comment>();
      Events = new HashSet<Event>();
      AssigneeEvents = new HashSet<Event>();
      AssignedIssues = new HashSet<Issue>();
      ClosedIssues = new HashSet<Issue>();
      Issues = new HashSet<Issue>();
      Milestones = new HashSet<Milestone>();
      Repositories = new HashSet<Repository>();
      AuthenticationTokens = new HashSet<AuthenticationToken>();
      Organizations = new HashSet<Account>();
      Members = new HashSet<Account>();
      AssignableRepositories = new HashSet<Repository>();
    }

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    [Required]
    [StringLength(4)]
    public string Type { get; set; }

    [StringLength(500)]
    public string AvatarUrl { get; set; }

    [Required]
    [StringLength(255)]
    public string Login { get; set; }

    [StringLength(255)]
    public string Name { get; set; }

    public long? MetaDataId { get; set; }

    [Required]
    public string ExtensionJson { get; set; }

    public long? RowVersion { get; set; }

    public virtual GitHubMetaData MetaData { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<AccessToken> AccessTokens { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Comment> Comments { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Event> Events { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Event> AssigneeEvents { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> AssignedIssues { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> ClosedIssues { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Milestone> Milestones { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> Repositories { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<AuthenticationToken> AuthenticationTokens { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Account> Organizations { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Account> Members { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> AssignableRepositories { get; set; }
  }
}
