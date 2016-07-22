namespace RealArtists.ShipHub.Common.DataModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;
  using Types;

  public class User : Account {
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Organization> Organizations { get; set; } = new HashSet<Organization>();

    public string RepoMetaJson {
      get { return RepositoryMetaData.SerializeObject(); }
      set { RepositoryMetaData = value.DeserializeObject<GitHubMetaData>(); }
    }

    [NotMapped]
    public GitHubMetaData RepositoryMetaData { get; set; }

    public string OrgMetaDataJson {
      get { return OrganizationMetaData.SerializeObject(); }
      set { OrganizationMetaData = value.DeserializeObject<GitHubMetaData>(); }
    }

    [NotMapped]
    public GitHubMetaData OrganizationMetaData { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Repository> AssignableRepositories { get; set; } = new HashSet<Repository>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<AccountRepository> LinkedRepositories { get; set; } = new HashSet<AccountRepository>();
  }
}
