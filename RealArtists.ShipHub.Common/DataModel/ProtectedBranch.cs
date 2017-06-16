using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RealArtists.ShipHub.Common.DataModel.Types;

namespace RealArtists.ShipHub.Common.DataModel {
  public class ProtectedBranch {
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long RepositoryId { get; set; }

    [Required]
    public string Name { get; set; }

    [Required]
    public string Protection { get; set; }

    [NotMapped]
    public GitHubMetadata ProtectionMetadata { get; set; }

    [Required]
    public string ProtectionMetadataJson {
      get => ProtectionMetadata.SerializeObject();
      set => ProtectionMetadata = value.DeserializeObject<GitHubMetadata>();
    }
  }
}
