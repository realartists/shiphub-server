namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class PingPayload {
    public string Zen { get; set; }
    public long HookId { get; set; }
    public Webhook Hook { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
