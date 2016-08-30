namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using Newtonsoft.Json;
  using Types;

  [Table("CacheMetadata")]
  public class CacheMetadata {
    public long Id { get; set; }

    [Required]
    [StringLength(255)]
    public string Key { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public string AccessToken { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public string LastRefresh { get; set; }

    [Required]
    public string MetadataJson {
      get {
        return Metadata.SerializeObject(Formatting.None);
      }
      set {
        Metadata = value.DeserializeObject<GitHubMetadata>();
      }
    }

    [NotMapped]
    public GitHubMetadata Metadata { get; set; }
  }
}
