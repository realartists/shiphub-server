namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class LabelPayload {
    public string Action { get; set; }
    public Label Label { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
