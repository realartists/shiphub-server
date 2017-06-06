namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class CommitCommentPayload {
    public string Action { get; set; }
    public CommitComment Comment { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
