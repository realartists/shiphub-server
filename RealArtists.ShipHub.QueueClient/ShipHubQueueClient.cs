namespace RealArtists.ShipHub.QueueClient {
  using System.Threading.Tasks;
  using Common.DataModel.Types;
  using Messages;

  public interface IShipHubQueueClient {
    Task NotifyChanges(IChangeSummary changeSummary);
    Task SyncAccount(long userId);
    Task SyncAccountRepositories(long userId);
    Task SyncRepositoryIssueTimeline(string repositoryFullName, int issueNumber, long forUserId);
    Task UpdateComplimentarySubscription(long userId);
  }

  public class ShipHubQueueClient : IShipHubQueueClient {
    IServiceBusFactory _factory;

    public ShipHubQueueClient(IServiceBusFactory serviceBusFactory) {
      _factory = serviceBusFactory;
    }

    public async Task NotifyChanges(IChangeSummary changeSummary) {
      var topic = _factory.TopicClientForName(ShipHubTopicNames.Changes);
      using (var bm = WebJobInterop.CreateMessage(new ChangeMessage(changeSummary))) {
        await topic.SendAsync(bm);
      }
    }

    public async Task SyncAccount(long userId) {
      var queue = _factory.QueueClientForName(ShipHubQueueNames.SyncAccount);

      var message = new UserIdMessage(userId);

      using (var bm = WebJobInterop.CreateMessage(message)) {
        await queue.SendAsync(bm);
      }
    }

    public async Task SyncAccountRepositories(long userId) {
      var message = new UserIdMessage(userId);

      var queue = _factory.QueueClientForName(ShipHubQueueNames.SyncAccountRepositories);
      using (var bm = WebJobInterop.CreateMessage(message)) {
        await queue.SendAsync(bm);
      }
    }

    public async Task SyncRepositoryIssueTimeline(string repositoryFullName, int issueNumber, long forUserId) {
      var queue = _factory.QueueClientForName(ShipHubQueueNames.SyncRepositoryIssueTimeline);

      var message = new IssueViewMessage(repositoryFullName, issueNumber, forUserId);

      using (var bm = WebJobInterop.CreateMessage(message)) {
        await queue.SendAsync(bm);
      }
    }

    public async Task UpdateComplimentarySubscription(long userId) {
      var queue = _factory.QueueClientForName(ShipHubQueueNames.BillingUpdateComplimentarySubscription);
      using (var bm = WebJobInterop.CreateMessage(new UserIdMessage(userId))) {
        await queue.SendAsync(bm);
      }
    }
  }
}
