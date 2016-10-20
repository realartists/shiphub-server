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

    public Task NotifyChanges(IChangeSummary changeSummary) {
      var topic = _factory.TopicClientForName(ShipHubTopicNames.Changes);
      using (var bm = WebJobInterop.CreateMessage(new ChangeMessage(changeSummary))) {
        return topic.SendAsync(bm);
      }
    }

    public Task AddOrUpdateRepoWebhooks(long targetId, long forUserId)
      => QueueIt(ShipHubQueueNames.AddOrUpdateRepoWebhooks, new TargetMessage(targetId, forUserId));

    public Task BillingGetOrCreatePersonalSubscription(long userId)
      => QueueIt(ShipHubQueueNames.BillingGetOrCreatePersonalSubscription, new UserIdMessage(userId));

    public Task BillingSyncOrgSubscriptionState(long targetId, long forUserId)
      => QueueIt(ShipHubQueueNames.BillingSyncOrgSubscriptionState, new TargetMessage(targetId, forUserId));

    public Task BillingUpdateComplimentarySubscription(long userId)
      => QueueIt(ShipHubQueueNames.BillingUpdateComplimentarySubscription, new UserIdMessage(userId));

    public Task SyncOrganizationMembers(long targetId, long forUserId)
      => QueueIt(ShipHubQueueNames.SyncOrganizationMembers, new TargetMessage(targetId, forUserId));

    public Task SyncRepository(long targetId, long forUserId)
      => QueueIt(ShipHubQueueNames.SyncRepository, new TargetMessage(targetId, forUserId));

    public Task SyncRepositoryIssueTimeline(string repositoryFullName, int issueNumber, long forUserId)
      => QueueIt(ShipHubQueueNames.SyncRepositoryIssueTimeline, new IssueViewMessage(repositoryFullName, issueNumber, forUserId));

    private Task QueueIt<T>(string queueName, T message) {
      var queue = _factory.QueueClientForName(queueName);
      using (var bm = WebJobInterop.CreateMessage(message)) {
        return queue.SendAsync(bm);
      }
    }
  }
}
