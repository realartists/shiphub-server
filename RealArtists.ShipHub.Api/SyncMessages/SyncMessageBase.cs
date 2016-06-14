namespace RealArtists.ShipHub.Api.SyncMessages {
  using Newtonsoft.Json;

  public class SyncMessageBase {
    [JsonProperty("msg")]
    public string MessageType { get; set; }
  }
}