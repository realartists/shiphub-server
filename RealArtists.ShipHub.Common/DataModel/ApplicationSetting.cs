namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class ApplicationSetting {
    [Key]
    [StringLength(255)]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string Id { get; set; }

    public string Value { get; set; }
  }
}
