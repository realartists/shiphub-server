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
    public GitHubMetadata Metadata { get; set; }

    [Required]
    public string MetadataJson {
      get => Metadata.SerializeObject();
      set => Metadata = value.DeserializeObject<GitHubMetadata>();
    }
  }
}
