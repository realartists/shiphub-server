namespace RealArtists.ShipHub.Common.GitHub.Models {
  using Newtonsoft.Json;

  public class Commit {
    // There is so much more than this, but for now, this is all we need.

    [JsonProperty("commit")]
    public CommitDetails CommitDetails { get; set; }

    public Account Author { get; set; }
    public Account Committer { get; set; }
  }

  public class CommitDetails {
    // There is so much more than this, but for now, this is all we need.
    public string Message { get; set; }
  }
}

