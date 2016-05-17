namespace RealArtists.ShipHub.QueueProcessor {
  using System.Threading.Tasks;
  using Common.DataModel;
  using Common.DataModel.Types;
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
        await context.BulkUpdateAccounts(message.ResponseDate, new[] { SharedMapper.Map<AccountTableType>(message.Value) });
      }
    }

    /// <summary>
    /// Precondition: None
    /// Postcondition: Repository and owner saved to DB
    /// </summary>
    public static async Task UpdateRepository(
      [ServiceBusTrigger(ShipHubQueueNames.UpdateRepository)] UpdateMessage<gh.Repository> message) {
      using (var context = new ShipHubContext()) {
        await context.BulkUpdateAccounts(message.ResponseDate, new[] { SharedMapper.Map<AccountTableType>(message.Value.Owner) });
        await context.BulkUpdateRepositories(message.ResponseDate, new[] { SharedMapper.Map<RepositoryTableType>(message.Value) });
      }
    }
  }
}
