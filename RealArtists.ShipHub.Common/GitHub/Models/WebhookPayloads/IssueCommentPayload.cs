namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class IssueCommentPayload {
    public string Action { get; set; }
    public Issue Issue { get; set; }
    public IssueComment Comment { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
