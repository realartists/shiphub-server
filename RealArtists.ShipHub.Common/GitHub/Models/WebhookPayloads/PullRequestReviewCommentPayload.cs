namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class PullRequestReviewCommentPayload {
    public string Action { get; set; }
    public PullRequestComment Comment { get; set; }
    public PullRequest PullRequest { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
