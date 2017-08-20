namespace RealArtists.ShipHub.Common.GitHub.Models.GraphQL {
  using System.Diagnostics.CodeAnalysis;
  using Newtonsoft.Json;

  public class User {
    [JsonProperty("databaseId")]
    public long Id { get; set; }

    [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
    [JsonProperty("__typename")]
    public GitHubAccountType Type { get; set; }

    public string Login { get; set; }

    public string Name { get; set; }
  }
}
