namespace RealArtists.ShipHub.Common.GitHub.Models.GraphQL {
  using System.Diagnostics.CodeAnalysis;
  using Newtonsoft.Json;

  public class User {
    [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
    public static readonly User Ghost = new User() {
      Id = 10137,
      Login = "ghost",
      Name = "ghost",
      Type = GitHubAccountType.User,
    };

    [JsonProperty("databaseId")]
    public long Id { get; set; }

    [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
    [JsonProperty("__typename")]
    public GitHubAccountType Type { get; set; }

    public string Login { get; set; }

    public string Name { get; set; }
  }
}
