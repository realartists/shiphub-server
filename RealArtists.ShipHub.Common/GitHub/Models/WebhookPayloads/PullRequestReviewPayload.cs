namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class PullRequestReviewPayload {
    public string Action { get; set; }
    public Review Review { get; set; }
    public PullRequest PullRequest { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
