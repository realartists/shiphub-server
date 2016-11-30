namespace RealArtists.ShipHub.Api.Sync.Messages {
  using System;

  public class RateLimitResponse : SyncMessageBase {
    public override string MessageType { get; set; } = "ratelimit";
    public DateTimeOffset Until { get; set; }
  }
}
