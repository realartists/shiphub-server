namespace GitHubUpdateProcessor {
  using System.IO;
  using System.Threading.Tasks;
  using Microsoft.Azure.WebJobs;
  using RealArtists.Ship.Server.QueueClient.GitHubSpider;
  using RealArtists.Ship.Server.QueueClient.GitHubUpdate;
  using RealArtists.ShipHub.Common;
  using RealArtists.ShipHub.Common.GitHub.Models;

  public static class Functions {
    // TODO: Locking?

    public static async Task SpiderToken(
      [QueueTrigger(SpiderQueueNames.AccessToken)] string token,
      [Queue(SpiderQueueNames.User)] IAsyncCollector<string> spiderUser,
      [Queue(GitHubQueueNames.Account)] IAsyncCollector<UpdateMessage<Account>> githubAccounts,
      [Queue(GitHubQueueNames.RateLimit)] IAsyncCollector<RateLimitUpdate> githubRateLimits,
      TextWriter log) {
      using (var g = GitHubSettings.CreateUserClient(token)) {
        var ur = await g.User();

        if (!ur.IsError) {
          await githubAccounts.Update(ur);
          await githubRateLimits.Update(ur);
          await spiderUser.AddAsync(ur.Result.Login);
        }
      }
    }

    public static async Task SpiderUser(
      [QueueTrigger(SpiderQueueNames.AccessToken)] string token,
      [Queue(SpiderQueueNames.User)] IAsyncCollector<string> spiderUser,
      [Queue(GitHubQueueNames.Account)] IAsyncCollector<UpdateMessage<Account>> githubAccounts,
      [Queue(GitHubQueueNames.RateLimit)] IAsyncCollector<RateLimitUpdate> githubRateLimits,
      TextWriter log) {
      using (var g = GitHubSettings.CreateUserClient(token)) {
        var ur = await g.User();

        if (!ur.IsError) {
          await githubAccounts.Update(ur);
          await githubRateLimits.Update(ur);
          await spiderUser.AddAsync(ur.Result.Login);
        }
      }
    }
  }
}
