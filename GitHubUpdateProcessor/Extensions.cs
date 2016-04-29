namespace GitHubUpdateProcessor {
  using System;
  using System.Threading.Tasks;
  using Microsoft.Azure.WebJobs;
  using RealArtists.Ship.Server.QueueClient.ResourceUpdate;
  using RealArtists.ShipHub.Common.GitHub;

  public static class Extensions {
    public static Task UpdateRateLimit(this IAsyncCollector<RateLimitUpdateMessage> collector, GitHubResponse response) {
      if (!string.IsNullOrWhiteSpace(response.Token)) {
        return collector.AddAsync(new RateLimitUpdateMessage() {
          AccessToken = response.Token,
          RateLimit = response.RateLimit,
          RateLimitRemaining = response.RateLimitRemaining,
          RateLimitReset = response.RateLimitReset,
        });
      } else {
        return Task.CompletedTask;
      }
    }

    public static T WithCacheMetaData<T>(this T message, GitHubResponse response)
      where T : UpdateMessage {
      var token = response.Token;
      if (token != null) {
        message.CacheMetaData = new CacheMetaData() {
          AccessToken = token,
          ETag = response.ETag,
          Expires = response.Expires,
          LastModified = response.LastModified,
        };
      }
      return message;
    }

    public static T WithWebhookMetaData<T>(this T message, Guid hookId, Guid deliveryId, string eventName)
      where T : UpdateMessage {
      message.WebhookMetaData = new WebhookMetaData() {
        DeliveryId = deliveryId,
        Event = eventName,
        HookId = hookId,
      };
      return message;
    }
  }
}
