namespace RealArtists.ShipHub.Common.DataModel {
  public partial class AccountRepository {
    public int AccountId { get; set; }

    public int RepositoryId { get; set; }

    public bool Hidden { get; set; }

    public virtual User Account { get; set; }

    public virtual Repository Repository { get; set; }
  }
}
