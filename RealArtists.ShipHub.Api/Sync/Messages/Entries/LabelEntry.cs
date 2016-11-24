namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  public class LabelEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Repository { get; set; }
    public string Color { get; set; }
    public string Name { get; set; }
  }
}
