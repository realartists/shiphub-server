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
    public UpdateMessage(T value) { Value = value; }
    public UpdateMessage(T value, GitHubCacheData cacheData) {
      Value = value;
      CacheData = cacheData;
    }

    public GitHubCacheData CacheData { get; set; }
    //public WebhookMetaData WebhookMetaData { get; set; }
    public T Value { get; set; }
  }
}
