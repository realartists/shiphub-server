namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations.Schema;

  [Table("OrganizationLog")]
  public class OrganizationLog {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    public long OrganizationId { get; set; }
    
    public long AccountId { get; set; }

    public bool Delete { get; set; }

    public long RowVersion { get; set; }
  }
}
