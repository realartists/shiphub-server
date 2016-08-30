namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class OrganizationAccount {
    [Key]
    [Column(Order = 0)]
    public long OrganizationId { get; set; }

    [Key]
    [Column(Order = 1)]
    public long UserId { get; set; }

    public virtual Organization Organization { get; set; }

    public virtual User User { get; set; }

    public bool Admin { get; set; }
  }
}
