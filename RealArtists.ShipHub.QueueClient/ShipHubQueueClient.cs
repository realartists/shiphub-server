namespace RealArtists.ShipHub.QueueClient {
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel.Types;
  using Messages;

  public interface IShipHubQueueClient {
    Task NotifyChanges(IChangeSummary changeSummary);
    Task AddOrUpdateOrgWebhooks(long targetId, long forUserId);
    Task AddOrUpdateRepoWebhooks(long targetId, long forUserId);
    Task BillingGetOrCreatePersonalSubscription(long userId);
    Task BillingSyncOrgSubscriptionState(long targetId, long forUserId);
    Task BillingUpdateComplimentarySubscription(long userId);
    Task QueueWebhookEvent(GitHubWebhookEventMessage message);
  }

  public class ShipHubQueueClient : IShipHubQueueClient {
    IServiceBusFactory _factory;

    public ShipHubQueueClient(IServiceBusFactory serviceBusFactory) {
      _factory = serviceBusFactory;
    }

    public Task AddOrUpdateOrgWebhooks(long targetId, long forUserId)
      => SendIt(ShipHubQueueNames.AddOrUpdateOrgWebhooks, new TargetMessage(targetId, forUserId));

    public Task AddOrUpdateRepoWebhooks(long targetId, long forUserId)
      => SendIt(ShipHubQueueNames.AddOrUpdateRepoWebhooks, new TargetMessage(targetId, forUserId));

    public Task BillingGetOrCreatePersonalSubscription(long userId)
      => SendIt(ShipHubQueueNames.BillingGetOrCreatePersonalSubscription, new UserIdMessage(userId));

    public Task BillingSyncOrgSubscriptionState(long targetId, long forUserId)
      => SendIt(ShipHubQueueNames.BillingSyncOrgSubscriptionState, new TargetMessage(targetId, forUserId));

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
