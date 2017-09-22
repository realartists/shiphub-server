namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  public class QueryEntry : SyncEntity {
    public string Identifier { get; set; }
    public AccountEntry Author { get; set; }
    public string Title { get; set; }
    public string Predicate { get; set; }
  }
}

  