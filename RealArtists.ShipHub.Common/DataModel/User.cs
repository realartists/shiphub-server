namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;
  using GitHub;
  using Types;

  public class User : Account {
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Organization> Organizations { get; set; } = new HashSet<Organization>();

    public string RepoMetadataJson {
      get { return RepositoryMetadata.SerializeObject(); }
      set { RepositoryMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata RepositoryMetadata { get; set; }

    public string OrgMetadataJson {
      get { return OrganizationMetadata.SerializeObject(); }
      set { OrganizationMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata OrganizationMetadata { get; set; }

    [StringLength(64)]
    public string Token { get; set; }

    [StringLength(255)]
    [Required(AllowEmptyStrings = true)]
    public string Scopes { get; set; } = "";

    public int RateLimit { get; set; } = 0;

    public int RateLimitRemaining { get; set; } = 0;

    public DateTimeOffset RateLimitReset { get; set; } = EpochUtility.EpochOffset;

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> AssignableRepositories { get; set; } = new HashSet<Repository>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<AccountRepository> LinkedRepositories { get; set; } = new HashSet<AccountRepository>();
  }
}
