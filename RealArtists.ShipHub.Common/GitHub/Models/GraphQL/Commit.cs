namespace RealArtists.ShipHub.Common.GitHub.Models.GraphQL {
  using Newtonsoft.Json;

  public class Commit {
    [JsonProperty("oid")]
    public string Id { get; set; }
  }
}
