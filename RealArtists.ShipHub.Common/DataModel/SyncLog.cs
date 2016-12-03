namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("SyncLog")]
  public class SyncLog {
    [Key]
    [ConcurrencyCheck]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long RowVersion { get; set; }

    [Required]
    [StringLength(4)]
    public string OwnerType { get; set; }

    public long OwnerId { get; set; }

    [Required]
    [StringLength(20)]
    public string ItemType { get; set; }

    public long ItemId { get; set; }

    public bool Delete { get; set; }
  }
}
