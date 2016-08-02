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

    public string MetadataJson {
      get { return Metadata.SerializeObject(); }
      set { Metadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata Metadata { get; set; }

    [StringLength(64)]
    public string Token { get; set; }

    [StringLength(255)]
    [Required(AllowEmptyStrings = true)]
    public string Scopes { get; set; } = "";

    public int RateLimit { get; set; } = 0;

    public int RateLimitRemaining { get; set; } = 0;

    public DateTimeOffset RateLimitReset { get; set; } = EpochUtility.EpochOffset;

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Comment> Comments { get; set; } = new HashSet<Comment>();

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
