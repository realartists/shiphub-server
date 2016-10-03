namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("Usage")]
  public class Usage {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column(Order = 0), Key]
    public long AccountId { get; set; }

    [Column(Order = 1), Key]
    public DateTimeOffset Date { get; set; }
  }
}
