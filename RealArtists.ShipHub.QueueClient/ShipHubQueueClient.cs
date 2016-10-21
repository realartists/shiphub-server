namespace RealArtists.ShipHub.QueueClient {
  using System.Threading.Tasks;
  using Common.DataModel.Types;
  using Messages;

  public interface IShipHubQueueClient {
    Task NotifyChanges(IChangeSummary changeSummary);
    Task AddOrUpdateRepoWebhooks(long targetId, long forUserId);
    Task BillingGetOrCreatePersonalSubscription(long userId);
    Task BillingSyncOrgSubscriptionState(long targetId, long forUserId);
    Task BillingUpdateComplimentarySubscription(long userId);
    Task SyncOrganizationMembers(long targetId, long forUserId);
    Task SyncRepository(long targetId, long forUserId);
    Task SyncRepositoryIssueTimeline(string repositoryFullName, int issueNumber, long forUserId);
  }

  public class ShipHubQueueClient : IShipHubQueueClient {
    IServiceBusFactory _factory;

    public ShipHubQueueClient(IServiceBusFactory serviceBusFactory) {
      _factory = serviceBusFactory;
    }

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

    public Task SyncOrganizationMembers(long targetId, long forUserId)
      => SendIt(ShipHubQueueNames.SyncOrganizationMembers, new TargetMessage(targetId, forUserId));

    public Task SyncRepository(long targetId, long forUserId)
      => SendIt(ShipHubQueueNames.SyncRepository, new TargetMessage(targetId, forUserId));

    public Task SyncRepositoryIssueTimeline(string repositoryFullName, int issueNumber, long forUserId)
      => SendIt(ShipHubQueueNames.SyncRepositoryIssueTimeline, new IssueViewMessage(repositoryFullName, issueNumber, forUserId));

    private async Task SendIt<T>(string queueName, T message) {
      var sender = await _factory.MessageSenderForName(queueName);
      using (var bm = WebJobInterop.CreateMessage(message)) {
        await sender.SendAsync(bm);
      }
    }
  }
}
