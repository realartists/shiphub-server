namespace GitHubUpdateProcessor {
  using System.IO;
  using System.Threading.Tasks;
  using Microsoft.Azure.WebJobs;
  using RealArtists.Ship.Server.QueueClient.GitHubSpider;
  using RealArtists.Ship.Server.QueueClient.ResourceUpdate;
  using RealArtists.ShipHub.Common;

  public static class SpiderHandler {
    // TODO: Locking?

    public static async Task SpiderToken(
      [QueueTrigger(SpiderQueueNames.AccessToken)] string token,
      [Queue(SpiderQueueNames.User)] IAsyncCollector<string> spiderUser,
      [Queue(ResourceQueueNames.Account)] IAsyncCollector<AccountUpdateMessage> githubAccounts,
      [Queue(ResourceQueueNames.RateLimit)] IAsyncCollector<RateLimitUpdateMessage> githubRateLimits,
      TextWriter log) {
      using (var g = GitHubSettings.CreateUserClient(token)) {
        var ur = await g.User();

        if (!ur.IsError) {
          await githubAccounts.AddAsync(new AccountUpdateMessage() {
            Value = ur.Result,
          }.WithCacheMetaData(ur));
          await githubRateLimits.UpdateRateLimit(ur);
          await spiderUser.AddAsync(ur.Result.Login);
        }
      }
    }

    public static async Task SpiderUser(
      [QueueTrigger(SpiderQueueNames.AccessToken)] string token,
      [Queue(SpiderQueueNames.User)] IAsyncCollector<string> spiderUser,
      [Queue(ResourceQueueNames.Account)] IAsyncCollector<AccountUpdateMessage> githubAccounts,
      [Queue(ResourceQueueNames.RateLimit)] IAsyncCollector<RateLimitUpdateMessage> githubRateLimits,
      TextWriter log) {
      using (var g = GitHubSettings.CreateUserClient(token)) {
        var ur = await g.User();

        if (!ur.IsError) {
          await githubRateLimits.UpdateRateLimit(ur);
        }
      }
    }
  }
}
