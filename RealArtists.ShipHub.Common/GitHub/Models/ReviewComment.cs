namespace RealArtists.ShipHub.Common.GitHub.Models {
  public class ReviewComment : Comment {
    public long? PullRequestReviewId { get; set; }
    public string DiffHunk { get; set; }
    public string Path { get; set; }
    public long Position { get; set; }
    public long OriginalPosition { get; set; }
    public string CommitId { get; set; }
    public string OriginalCommitId { get; set; }
    public long InReplyTo { get; set; }

    private string _pullRequestUrl;
    public string PullRequestUrl {
      get => _pullRequestUrl;
      set {
        _pullRequestUrl = value;
        IssueNumber = null;

        if (!_pullRequestUrl.IsNullOrWhiteSpace()) {
          var parts = _pullRequestUrl.Split('/');
          // construct issueUrl
          parts[parts.Length - 2] = "issues";
          IssueUrl = string.Join("/", parts);
        }
      }
    }
  }
}
