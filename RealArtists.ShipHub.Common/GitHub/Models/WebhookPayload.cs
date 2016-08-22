namespace RealArtists.ShipHub.Common.GitHub.Models {
  public class WebhookPayload {
    public string Action { get; set; }
    public Issue Issue { get; set; }
    public Comment Comment { get; set; }
    public Repository Repository { get; set; }
    public Account Organization { get; set; }
  }
}