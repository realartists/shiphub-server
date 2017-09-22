namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  public class DeletedEntry : SyncEntity {
    public long Identifier { get; set; }
  }

  public class DeletedGuidEntry : SyncEntity {
    public string Identifier { get; set; }
  }
}
