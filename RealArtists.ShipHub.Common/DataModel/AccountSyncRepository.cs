namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using RealArtists.ShipHub.Common.DataModel.Types;

  public class AccountSyncRepository {
    [Key]
    [Column(Order = 0)]
    public long AccountId { get; set; }

    [Key]
    [Column(Order = 1)]
    public long RepositoryId { get; set; }

    [Column("RepoMetadataJson")]
    public string RepoMetadataJson {
      get => RepositoryMetadata.SerializeObject();
      set => RepositoryMetadata = value.DeserializeObject<GitHubMetadata>();
    }

    [NotMapped]
    public GitHubMetadata RepositoryMetadata { get; set; }

    public virtual User Account { get; set; }

    public virtual Repository Repository { get; set; }
  }
}
