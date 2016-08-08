namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;

  public class ReactionEntry : SyncEntity {
    public long Identifier { get; set; }
    public long User { get; set; }
    public long? Issue { get; set; }
    public long? Comment { get; set; }
    public string Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
  }
}
