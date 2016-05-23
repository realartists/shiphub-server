namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public partial class AccountRepository {
    [Key]
    [Column(Order = 0)]
    public long AccountId { get; set; }

    [Key]
    [Column(Order = 1)]
    public long RepositoryId { get; set; }

    public bool Hidden { get; set; }

    public virtual User Account { get; set; }

    public virtual Repository Repository { get; set; }
  }
}
