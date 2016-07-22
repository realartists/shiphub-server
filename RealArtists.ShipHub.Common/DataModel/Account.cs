namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;
  using GitHub;
  using Types;

  public abstract class Account {
    public const string OrganizationType = "org";
    public const string UserType = "user";

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    [Required]
    [StringLength(255)]
    public string Login { get; set; }

    public DateTimeOffset Date { get; set; }

    public string MetaDataJson {
      get { return MetaData.SerializeObject(); }
      set { MetaData = value.DeserializeObject<GitHubMetaData>(); }
    }

    [NotMapped]
    public GitHubMetaData MetaData { get; set; }

    // Most of these really only apply to users, but GitHub allows users to convert to orgs
    // so some of these may exist from before the conversion.

    [StringLength(64)]
    public string Token { get; set; }

    [Required(AllowEmptyStrings = true)]
    [StringLength(255)]
    public string Scopes { get; set; } = "";

    public int RateLimit { get; set; } = 0;

    public int RateLimitRemaining { get; set; } = 0;

    public DateTimeOffset RateLimitReset { get; set; } = EpochUtility.EpochOffset;

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

    // This applies to users and orgs
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> OwnedRepositories { get; set; } = new HashSet<Repository>();
  }
}
