using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json.Linq;
using RealArtists.ShipHub.Common.DataModel.Types;

namespace RealArtists.ShipHub.Common.DataModel {
  public class ProtectedBranch {
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    public long RepositoryId { get; set; }

    [Required]
    public string Name { get; set; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [NotMapped]
    public IDictionary<string, JToken> ProtectionDictionary {
      get => Protection.DeserializeObject<IDictionary<string, JToken>>();
      set => Protection = value.SerializeObject();
    }

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
