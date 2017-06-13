namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class PullRequestPayload {
    public string Action { get; set; }
    public int Number { get; set; }
    public PullRequest PullRequest { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
