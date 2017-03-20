namespace RealArtists.ShipHub.QueueClient.Messages {
  using System;

  public class GitHubWebhookEventMessage {
    public string EntityType { get; set; }
    public long EntityId { get; set; }
    public string EventName { get; set; }
    public Guid DeliveryId { get; set; }
    public string Payload { get; set; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    public byte[] Signature { get; set; }
  }
}
