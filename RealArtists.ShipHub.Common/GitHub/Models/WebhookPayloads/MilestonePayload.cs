namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  public class MilestonePayload {
    public string Action { get; set; }
    public Milestone Milestone { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
