namespace RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads {
  using System;

  public class StatusPayload {
    public long Id { get; set; }
    public string Sha { get; set; }
    public string Name { get; set; }
    public string TargetUrl { get; set; }
    public string Context { get; set; }
    public string Description { get; set; }
    public string State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
    public Account Sender { get; set; }
  }
}
