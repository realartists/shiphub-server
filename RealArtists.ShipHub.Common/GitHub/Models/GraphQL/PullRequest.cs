namespace RealArtists.ShipHub.Common.GitHub.Models.GraphQL {
  using Newtonsoft.Json;

  public class PullRequest {
    [JsonProperty("databaseId")]
    public long Id { get; set; }

    public PullRequestReviewConnection Reviews { get; set; }
  }
}
