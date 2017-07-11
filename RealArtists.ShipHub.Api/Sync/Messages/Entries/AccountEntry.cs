namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  public class AccountEntry : SyncEntity {
    public long Identifier { get; set; }
    public string Login { get; set; }
    public string Name { get; set; }
  }
}
