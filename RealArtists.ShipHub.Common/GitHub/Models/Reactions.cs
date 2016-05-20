namespace RealArtists.ShipHub.Common.GitHub.Models {
  using Newtonsoft.Json;

  public class Reactions {
    public int TotalCount { get; set; }
    public int Laugh { get; set; }
    public int Confused { get; set; }
    public int Heart { get; set; }
    public int Hooray { get; set; }
    public int PlusOne { get; set; }
    public int MinusOne { get; set; }

    [JsonProperty("+1")]
    private int PlusOneSurrogate { set { PlusOne = value; } }

    [JsonProperty("-1")]
    private int MinusOneSurrogate { set { MinusOne = value; } }
  }
}
