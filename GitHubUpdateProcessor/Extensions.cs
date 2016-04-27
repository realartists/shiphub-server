namespace GitHubUpdateProcessor {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using Microsoft.Azure.WebJobs;
  using RealArtists.Ship.Server.QueueClient.GitHubUpdate;
  using RealArtists.ShipHub.Common.GitHub;

  public static class Extensions {
    public static Task Update(this IAsyncCollector<RateLimitUpdate> collector, GitHubResponse response) {
      return collector.AddAsync(new RateLimitUpdate() {
        AccessToken = response.Credentials.Parameter,
        RateLimit = response.RateLimit,
        RateLimitRemaining = response.RateLimitRemaining,
        RateLimitReset = response.RateLimitReset,
      });
    }

    public static Task Update<T>(this IAsyncCollector<UpdateMessage<T>> collector, GitHubResponse<T> response) {
      var message = new UpdateMessage<T>() {
        Value = response.Result,
      };

      var token = response.Credentials?.Parameter;
      if (token != null) {
        message.CacheMetaData = new CacheMetaData() {
          AccessToken = token,
          ETag = response.ETag,
          Expires = response.Expires,
          LastModified = response.LastModified,
        };
      }

      return collector.AddAsync(message);
    }
  }
}
