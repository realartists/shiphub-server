namespace RealArtists.Ship.Server.QueueClient.GitHubUpdate {
  using System;

  public class CacheMetaData {
    public string AccessToken { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? Expires { get; set; }
  }

  public class WebhookMetaData {
    public Guid HookId { get; set; }
    public Guid DeliveryId { get; set; }
    public string Event { get; set; }
  }

  public class UpdateMessage<T> {
    public CacheMetaData CacheMetaData { get; set; }
    public WebhookMetaData WebhookMetaData { get; set; }
    public T Value { get; set; }
  }
}
