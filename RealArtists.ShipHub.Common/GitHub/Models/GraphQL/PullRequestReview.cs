namespace RealArtists.ShipHub.Common.GitHub.Models.GraphQL {
  using System;
  using Newtonsoft.Json;

  public class PullRequestReview {
    [JsonProperty("databaseId")]
    public long Id { get; set; }

    public User Author { get; set; }

    public string Body { get; set; }

    public Commit Commit { get; set; }

    public string State { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
  }
}
