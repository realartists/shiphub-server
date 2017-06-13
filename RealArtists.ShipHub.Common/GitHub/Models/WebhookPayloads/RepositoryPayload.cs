namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class RepositoryPayload {
    public string Action { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
