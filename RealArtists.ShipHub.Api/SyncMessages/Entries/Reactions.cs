namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using Newtonsoft.Json;

  public class Reactions {
    public int TotalCount { get; set; }
    public int Laugh { get; set; }
    public int Confused { get; set; }
    public int Heart { get; set; }
    public int Hooray { get; set; }

    [JsonProperty("+1")]
    public int PlusOne { get; set; }

    [JsonProperty("-1")]
    public int MinusOne { get; set; }
  }
}
