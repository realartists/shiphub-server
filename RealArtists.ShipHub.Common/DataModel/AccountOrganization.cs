namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class AccountOrganization {
    [Key]
    [Column(Order = 0)]
    public long UserId { get; set; }

    public virtual User User { get; set; }

    [Key]
    [Column(Order = 1)]
    public long OrganizationId { get; set; }

    public virtual Organization Organization { get; set; }

    public bool Admin { get; set; }
  }
}