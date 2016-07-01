namespace RealArtists.ShipHub.Api.Sync.Messages {
  using Newtonsoft.Json;

  public class SyncMessageBase {
    [JsonProperty("msg")]
    public virtual string MessageType { get; set; }
  }
}
