namespace RealArtists.ShipHub.Common.DataModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;
  using Types;

  public class Organization : Account {
    // Yeah, this is a little gross.
    [Column("RepoMetadataJson")]
    public string MemberMetadataJson {
      get { return MemberMetadata.SerializeObject(); }
      set { MemberMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata MemberMetadata { get; set; }

    public string ProjectMetadataJson {
      get { return ProjectMetadata.SerializeObject(); }
      set { ProjectMetadata = value.DeserializeObject<GitHubMetadata>(); }
    }

    [NotMapped]
    public GitHubMetadata ProjectMetadata { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<OrganizationAccount> OrganizationAccounts { get; set; } = new HashSet<OrganizationAccount>();

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Project> Projects { get; set; } = new HashSet<Project>();
  }
}
