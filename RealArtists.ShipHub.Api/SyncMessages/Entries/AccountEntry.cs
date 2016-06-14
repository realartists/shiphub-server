namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  public class AccountEntry : SyncEntity {
    public long Identifier { get; set; }
    public string Login { get; set; }
  }
}
