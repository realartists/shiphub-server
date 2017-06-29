namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;

  public class CommitStatusEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Repository { get; set; }
    public string Reference { get; set; }
    public string State { get; set; }
    public string TargetUrl { get; set; }
    public string Description { get; set; }
    public string Context { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
  }
}
