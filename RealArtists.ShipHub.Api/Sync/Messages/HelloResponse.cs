namespace RealArtists.ShipHub.Api.Sync.Messages {
  using System;

  public class HelloResponse : SyncMessageBase {
    public override string MessageType { get; set; } = "hello";
    public Guid PurgeIdentifier { get; set; }
    public UpgradeDetails Upgrade { get; set; } = new UpgradeDetails() { Required = false };
  }

  public class UpgradeDetails {
    public bool Required { get; set; }
  }
}
