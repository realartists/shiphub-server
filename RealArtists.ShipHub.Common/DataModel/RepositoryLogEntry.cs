namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("RepositoryLog")]
  public class RepositoryLogEntry {
    public long Id { get; set; }

    public long RepositoryId { get; set; }

    [Required]
    [StringLength(20)]
    public string Type { get; set; }

    public long ItemId { get; set; }

    public bool Delete { get; set; }

    /// <summary>
    /// This is nullable in the DB, but since log entries are never created by EF
    /// all entries it sees will be non-nullable.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public long RowVersion { get; set; }

    public virtual Repository Repository { get; set; }
  }
}
