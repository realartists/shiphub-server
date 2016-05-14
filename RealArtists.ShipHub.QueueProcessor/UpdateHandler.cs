namespace RealArtists.ShipHub.QueueProcessor {
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using Common.DataModel;
  using Microsoft.Azure.WebJobs;
  using QueueClient;
  using QueueClient.Messages;
  using gh = Common.GitHub.Models;

  // TODO: The following summary is now completely false.

  /// <summary>
  /// It sucks that this class exists.
  /// 
  /// GitHub doesn't have a way to subscribe to all the notifications we want, so we
  /// have to poll. But we don't want to poll everything. And even the things we have
  /// to poll, we don't want to poll frequently.
  /// 
  /// When we poll and get complete data for one resource, we often learn about other
  /// resources as a side effect. This bonus information may already be known, but
  /// it can also be new or hint that we should poll a related item sooner than we
  /// had planned.
  /// 
  /// So we send all bonus information here. We may discard it, create stubs, and/or
  /// trigger an immediate complete update.
  /// 
  /// If possible, this class should never trigger notifications. Only the complete
  /// updates should do that.
  /// </summary>
  public static class UpdateHandler {
    // TODO: Notifications

    /* TODO generally:
     * If updated with token and cache metadata is available, record it.
     * If not, wipe it.
     * When updated via a webhook, update last seen for the hook, and mark the item as refreshed by the hook.
     * Notifications if changed
     */

    /// <summary>
    /// Precondition: None
    /// Postcondition: Account saved to DB.
    /// </summary>
    public static async Task UpdateAccount(
      [ServiceBusTrigger(ShipHubQueueNames.UpdateAccount)] UpdateMessage<gh.Account> message) {
      using (var context = new ShipHubContext()) {
        var logic = new DataLogic(context);

        await logic.UpdateOrStubAccount(message.Value, message.ResponseDate);

        if (context.ChangeTracker.HasChanges()) {
          await context.SaveChangesAsync();
        }
      }
    }

    /// <summary>
    /// Precondition: None
    /// Postcondition: Repository and owner saved to DB
    /// </summary>
    public static async Task UpdateRepository(
      [ServiceBusTrigger(ShipHubQueueNames.UpdateRepository)] UpdateMessage<gh.Repository> message) {
      using (var context = new ShipHubContext()) {
        var logic = new DataLogic(context);

        await logic.UpdateOrStubRepository(message.Value, message.ResponseDate);

        if (context.ChangeTracker.HasChanges()) {
          await context.SaveChangesAsync();
        }
      }
    }

    /// <summary>
    /// Precondition: Account and repositories exist.
    /// Postcondition: Account is linked to the specified repositories.
    /// </summary>
    public static async Task UpdateAccountRepositories(
      [ServiceBusTrigger(ShipHubQueueNames.UpdateRepositoryAssignable)] UpdateMessage<AccountRepositoriesMessage> message,
      TextWriter logger) {
      using (var context = new ShipHubContext()) {
        var update = message.Value;

        // TODO: Check and abort or Update MetaData

        // Bulk update linked accounts in a single shot.
        await context.UpdateAccountLinkedRepositories(
          update.AccountId,
          update.LinkedRepositoryIds);
      }
    }

    public static async Task UpdateRepositoryAssignable(
      [ServiceBusTrigger(ShipHubQueueNames.UpdateRepositoryAssignable)] UpdateMessage<RepositoryAssignableMessage> message) {
      var update = message.Value;
      using (var context = new ShipHubContext()) {
        var logic = new DataLogic(context);

        var repo = await logic.UpdateOrStubRepository(message.Value.Repository, message.ResponseDate);

        // Ensure repo and owner are saved if new.
        if (context.ChangeTracker.HasChanges()) {
          await context.SaveChangesAsync();
        }

        // Bulk update assignable users in a single shot.
        await context.UpdateRepositoryAssignableAccounts(
          repo.Id,
          update.AssignableAccounts.Select(x => new AccountStubTableRow() {
            Id = x.Id,
            Type = x.Type == gh.GitHubAccountType.Organization ? Account.OrganizationType : Account.UserType,
            Login = x.Login,
          }));
      }
    }
  }
}
