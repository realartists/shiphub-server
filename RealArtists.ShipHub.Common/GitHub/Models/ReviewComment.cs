namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using Newtonsoft.Json;

  public class ReviewComment {
    public long Id { get; set; }
    public long PullRequestReviewId { get; set; }
    public string DiffHunk { get; set; }
    public string Path { get; set; }
    public long? Position { get; set; }
    public long? OriginalPosition { get; set; }
    public string CommitId { get; set; }
    public string OriginalCommitId { get; set; }
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Account User { get; set; }
    public ReactionSummary Reactions { get; set; }

    // Undocumented. Of course.
    public long? InReplyTo { get; set; }

    private string _pullRequestUrl;
    public string PullRequestUrl {
      get => _pullRequestUrl;
      set {
        _pullRequestUrl = value;
        PullRequestNumber = null;

        if (!_pullRequestUrl.IsNullOrWhiteSpace()) {
          var parts = _pullRequestUrl.Split('/');
          PullRequestNumber = int.Parse(parts[parts.Length - 1]);
        }
      }
    }

    [JsonIgnore]
    public int? PullRequestNumber { get; set; }
  }
}
