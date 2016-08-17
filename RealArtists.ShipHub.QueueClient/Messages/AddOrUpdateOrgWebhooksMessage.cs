namespace RealArtists.ShipHub.QueueClient.Messages {
  public class AddOrUpdateOrgWebhooksMessage {
    public long OrganizationId { get; set; }
    public string AccessToken { get; set; }
  }
}
