namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;

  [Table("RepositoryLog")]
  public class RepositoryLog {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    public long RepositoryId { get; set; }

    [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
    [Required]
    [StringLength(20)]
    public string Type { get; set; }

    public long ItemId { get; set; }

    public bool Delete { get; set; }

    public long RowVersion { get; set; }
  }
}
