namespace RealArtists.ShipHub.Api.Sync.Messages {
  using System;
  using Common;

  public class HelloResponse : SyncMessageBase {
    public override string MessageType { get; set; } = "hello";
    public Guid PurgeIdentifier { get; set; }
    public UpgradeDetails Upgrade { get; set; } = new UpgradeDetails() { Required = false };
    public long Version { get; } = Constants.ServerVersion;
  }

  public class UpgradeDetails {
    public bool Required { get; set; }
  }
}
