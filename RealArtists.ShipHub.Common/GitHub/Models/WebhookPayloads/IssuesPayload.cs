namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class IssuesPayload {
    public string Action { get; set; }
    public Issue Issue { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
