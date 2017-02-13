namespace RealArtists.ShipHub.Common.DataModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;
  using Types;

  public class User : Account {
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<OrganizationAccount> AccountOrganizations { get; set; } = new HashSet<OrganizationAccount>();

    [Column("RepoMetadataJson")]
    public string RepoMetadataJson {
      get { return RepositoryMetadata.SerializeObject(); }
      set { RepositoryMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata RepositoryMetadata { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> AssignableRepositories { get; set; } = new HashSet<Repository>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<AccountRepository> LinkedRepositories { get; set; } = new HashSet<AccountRepository>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<GitHubToken> Tokens { get; set; } = new HashSet<GitHubToken>();
  }
}
