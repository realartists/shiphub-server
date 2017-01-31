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
    Task QueueWebhookEvent(GitHubWebhookEventMessage message);
  }

  public class ShipHubQueueClient : IShipHubQueueClient {
    IServiceBusFactory _factory;

    public ShipHubQueueClient(IServiceBusFactory serviceBusFactory) {
      _factory = serviceBusFactory;
    }

    public Task BillingGetOrCreatePersonalSubscription(long userId)
      => SendIt(ShipHubQueueNames.BillingGetOrCreatePersonalSubscription, new UserIdMessage(userId));

    public Task BillingSyncOrgSubscriptionState(IEnumerable<long> orgIds, long forUserId)
      => SendIt(ShipHubQueueNames.BillingSyncOrgSubscriptionState, new SyncOrgSubscriptionStateMessage(orgIds, forUserId));

    public Task BillingUpdateComplimentarySubscription(long userId)
      => SendIt(ShipHubQueueNames.BillingUpdateComplimentarySubscription, new UserIdMessage(userId));

    public Task NotifyChanges(IChangeSummary changeSummary)
      => SendIt(ShipHubTopicNames.Changes, new ChangeMessage(changeSummary));

    public Task QueueWebhookEvent(GitHubWebhookEventMessage message)
      => SendIt(ShipHubQueueNames.WebhooksEvent, message);

    private async Task SendIt<T>(string queueName, T message) {
      Log.Debug(() => $"{queueName} - {message}");
      var sender = await _factory.MessageSenderForName(queueName);
      using (var bm = WebJobInterop.CreateMessage(message)) {
        await sender.SendAsync(bm);
      }
    }
  }
}
