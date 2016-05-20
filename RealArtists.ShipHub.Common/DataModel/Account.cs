namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;

  public abstract partial class Account  {
    public const string OrganizationType = "org";
    public const string UserType = "user";

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    [StringLength(500)]
    public string AvatarUrl { get; set; }

    [Required]
    [StringLength(255)]
    public string Login { get; set; }

    public DateTimeOffset Date { get; set; }

    public long? RepositoryMetaDataId { get; set; }

    [ConcurrencyCheck]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public long? RowVersion { get; set; }

    public virtual GitHubMetaData RepositoryMetaData { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<AccessToken> AccessTokens { get; set; } = new HashSet<AccessToken>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Comment> Comments { get; set; } = new HashSet<Comment>();

    //[SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    //public virtual ICollection<IssueEvent> Events { get; set; } = new HashSet<IssueEvent>();

    //[SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    //public virtual ICollection<IssueEvent> AssigneeEvents { get; set; } = new HashSet<IssueEvent>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> AssignedIssues { get; set; } = new HashSet<Issue>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> ClosedIssues { get; set; } = new HashSet<Issue>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; } = new HashSet<Issue>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<AuthenticationToken> AuthenticationTokens { get; set; } = new HashSet<AuthenticationToken>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> AssignableRepositories { get; set; } = new HashSet<Repository>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<AccountRepository> LinkedRepositories { get; set; } = new HashSet<AccountRepository>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> OwnedRepositories { get; set; } = new HashSet<Repository>();
  }
}
