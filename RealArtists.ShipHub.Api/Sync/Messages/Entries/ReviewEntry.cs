namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;

  public class ReviewEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Issue { get; set; }
    public long User { get; set; }
    public string Body { get; set; }
    public string CommitId { get; set; }
    public string State { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
  }
}
