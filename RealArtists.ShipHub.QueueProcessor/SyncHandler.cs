namespace RealArtists.ShipHub.QueueProcessor {
  using System.Linq;
  using System.Threading.Tasks;
  using Microsoft.Azure.WebJobs;
  using QueueClient;
  using QueueClient.Messages;
  using Common;
  using gh = Common.GitHub.Models;

  public static class SyncHandler {
    /// <summary>
    /// Precondition: None.
    /// Postcondition: User saved in DB.
    /// </summary>
    public static async Task SyncAccount(
      [QueueTrigger(ShipHubQueueNames.SyncAccount)] SyncAccountMessage message,
      [Queue(ShipHubQueueNames.SyncAccountRepositories)] IAsyncCollector<SyncAccountRepositoriesMessage> syncAccountRepos) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);

      var userResponse = await ghc.User();
      var user = userResponse.Result;
      await UpdateHandler.UpdateAccount(new UpdateMessage<gh.Account>(user, userResponse.Date, userResponse.CacheData));

      // Now that the user is saved in the DB, safe to sync all repos.
      await syncAccountRepos.AddAsync(new SyncAccountRepositoriesMessage() {
        AccessToken = message.AccessToken,
        Account = user,
      });
    }

    /// <summary>
    /// Precondition: User saved in DB.
    /// Postcondition: User's repos saved in DB.
    /// </summary>
    public static async Task SyncAccountRepositories(
      [QueueTrigger(ShipHubQueueNames.SyncAccountRepositories)] SyncAccountRepositoriesMessage message,
      [Queue(ShipHubQueueNames.SyncRepository)] IAsyncCollector<SyncRepositoryMessage> syncRepo,
      [Queue(ShipHubQueueNames.UpdateAccountRepositories)] IAsyncCollector<UpdateMessage<AccountRepositoriesMessage>> updateAccountRepos) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);

      // Get eligible repos
      var repoResponse = await ghc.Repositories();
      var reposWithIssues = repoResponse.Result.Where(x => x.HasIssues);
      var assignableRepos = reposWithIssues.ToDictionary(x => x.FullName, x => ghc.Assignable(x.FullName, message.Account.Login));
      await Task.WhenAll(assignableRepos.Values.ToArray());
      var keepRepos = reposWithIssues.Where(x => assignableRepos[x.FullName].Result.Result).ToArray();

      // Save eligible repos
      var saveTasks = keepRepos
        .Select(x => UpdateHandler.UpdateRepository(new UpdateMessage<gh.Repository>(x, repoResponse.Date)))
        .ToArray();
      await Task.WhenAll(saveTasks);

      // TODO: Update user repoMetaData

      // Save Account->Repository mapping
      await updateAccountRepos.AddAsync(new UpdateMessage<AccountRepositoriesMessage>(
        new AccountRepositoriesMessage() {
        },
        repoResponse.Date,
        repoResponse.CacheData));

      var syncTasks = keepRepos.Select(x => syncRepo.AddAsync(new SyncRepositoryMessage() {
        AccessToken = message.AccessToken,
        Repository = x,
      })).ToArray();

      await Task.WhenAll(syncTasks);
    }

    /// <summary>
    /// Precondition: Repos saved in DB
    /// Postcondition: None.
    /// </summary>
    public static async Task SyncRepository(
      [QueueTrigger(ShipHubQueueNames.SyncRepository)] SyncRepositoryMessage message,
      [Queue(ShipHubQueueNames.SyncRepositoryAssignees)] IAsyncCollector<SyncRepositoryMessage> syncRepoAssignees,
      [Queue(ShipHubQueueNames.SyncRepositoryIssues)] IAsyncCollector<SyncRepositoryMessage> syncRepoMessages) {
      await Task.CompletedTask;
    }
  }

  
}
