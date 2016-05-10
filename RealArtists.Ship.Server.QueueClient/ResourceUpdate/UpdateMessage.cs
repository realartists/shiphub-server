namespace RealArtists.Ship.Server.QueueClient.ResourceUpdate {
  using System;
  using ShipHub.Common.GitHub;

  //public class WebhookMetaData {
  //  public Guid HookId { get; set; }
  //  public Guid DeliveryId { get; set; }
  //  public string Event { get; set; }
  //}

  public class UpdateMessage<T> {
    public UpdateMessage() { }
    public UpdateMessage(T value, DateTimeOffset responseDate) : this(value, responseDate, null) { }
    public UpdateMessage(T value, DateTimeOffset responseDate, GitHubCacheData cacheData) {
      CacheData = cacheData;
      ResponseDate = responseDate;
      Value = value;
    }

    public GitHubCacheData CacheData { get; set; }
    //public WebhookMetaData WebhookMetaData { get; set; }
    public DateTimeOffset ResponseDate { get; set; }
    public T Value { get; set; }
  }
}
