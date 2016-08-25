namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class CacheMetadata {
    public long Id { get; set; }

    [Required]
    [StringLength(255)]
    public string Key { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public string AccessToken { get; set; }

    [Required]
    public string MetadataJson { get; set; }
  }
}
