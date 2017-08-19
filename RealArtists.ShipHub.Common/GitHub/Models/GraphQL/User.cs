namespace RealArtists.ShipHub.Common.GitHub.Models.GraphQL {
  using Newtonsoft.Json;

  public class User {
    [JsonProperty("databaseId")]
    public long Id { get; set; }

    public string Login { get; set; }

    public string Name { get; set; }
  }
}
