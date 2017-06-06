namespace RealArtists.ShipHub.QueueClient {
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel.Types;
  using Messages;

  public interface IShipHubQueueClient {
    Task NotifyChanges(IChangeSummary changeSummary);
    Task BillingGetOrCreatePersonalSubscription(long userId);
    Task BillingSyncOrgSubscriptionState(IEnumerable<long> orgIds, long forUserId);
    Task BillingUpdateComplimentarySubscription(long userId);
  }

  public static class ShipHubQueueClientExtensions {
    public static Task Submit(this IChangeSummary summary, IShipHubQueueClient client) {
      if (summary == null || summary.IsEmpty) {
        return Task.CompletedTask;
      }

      return client.NotifyChanges(summary);
    }
  }

  public class ShipHubQueueClient : IShipHubQueueClient {
    IServiceBusFactory _factory;

    public ShipHubQueueClient(IServiceBusFactory serviceBusFactory) {
      _factory = serviceBusFactory;
    }

    public Task BillingGetOrCreatePersonalSubscription(long userId) {
      return SendIt(ShipHubQueueNames.BillingGetOrCreatePersonalSubscription, new UserIdMessage(userId));
    }

    public Task BillingSyncOrgSubscriptionState(IEnumerable<long> orgIds, long forUserId) {
      return SendIt(ShipHubQueueNames.BillingSyncOrgSubscriptionState, new SyncOrgSubscriptionStateMessage(orgIds, forUserId));
    }

    public Task BillingUpdateComplimentarySubscription(long userId) {
      return SendIt(ShipHubQueueNames.BillingUpdateComplimentarySubscription, new UserIdMessage(userId));
    }

    public Task NotifyChanges(IChangeSummary changeSummary) {
      return SendIt(ShipHubTopicNames.Changes, new ChangeMessage(changeSummary));
    }

    private async Task SendIt<T>(string queueName, T message) {
      Log.Debug(() => $"{queueName} - {message}");
      var sender = await _factory.MessageSenderForName(queueName);
      using (var bm = WebJobInterop.CreateMessage(message)) {
        await sender.SendAsync(bm);
      }
    }
  }
}
