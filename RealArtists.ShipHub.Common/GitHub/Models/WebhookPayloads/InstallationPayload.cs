namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class InstallationPayload {
    public string Action { get; set; } // created or deleted
    public Installation Installation { get; set; }
    public Account Sender { get; set; }
  }
}
