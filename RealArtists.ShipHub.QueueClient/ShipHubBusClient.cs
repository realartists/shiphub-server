namespace RealArtists.ShipHub.QueueClient {
  using System;
  using System.Collections.Concurrent;
  using System.Linq;
  using System.Threading.Tasks;
  using Common.DataModel.Types;
  using Messages;
  using Microsoft.Azure;
  using Microsoft.ServiceBus;
  using Microsoft.ServiceBus.Messaging;

  public interface IShipHubBusClient {
    Task NotifyChanges(IChangeSummary changeSummary);
    Task SyncAccount(string accessToken);
    Task SyncAccountRepositories(long accountId, string login, string accessToken);
    Task SyncRepositoryIssueTimeline(string accessToken, string repositoryFullName, int issueNumber);
  }

  public class ShipHubBusClient : IShipHubBusClient {
    static readonly string _connString = CloudConfigurationManager.GetSetting("AzureWebJobsServiceBus");
    static readonly NamespaceManager _namespaceManager = NamespaceManager.CreateFromConnectionString(_connString);
    static ConcurrentDictionary<string, QueueClient> _queueClients = new ConcurrentDictionary<string, QueueClient>();
    static ConcurrentDictionary<string, TopicClient> _topicClients = new ConcurrentDictionary<string, TopicClient>();

    static T CacheLookup<T>(ConcurrentDictionary<string, T> cache, string key, Func<T> valueCreator)
      where T : class {
      T client = null;
      if (!cache.TryGetValue(key, out client)) {
        client = valueCreator();
        cache.TryAdd(key, client);
      }
      return client;
    }

    static QueueClient QueueClientForName(string queueName) {
      return CacheLookup(_queueClients, queueName, () => QueueClient.CreateFromConnectionString(_connString, queueName));
    }

    static TopicClient TopicClientForName(string topicName) {
      return CacheLookup(_topicClients, topicName, () => TopicClient.CreateFromConnectionString(_connString, topicName));
    }

    // TODO: Non-public
    public static async Task<SubscriptionClient> SubscriptionClientForName(string topicName, string subscriptionName = null) {
      // For auto expiring subscriptions we only care that the names never overlap
      if (string.IsNullOrWhiteSpace(subscriptionName)) {
        subscriptionName = Guid.NewGuid().ToString("N");
      }

      // ensure the subscription exists
      // safe to do this every time even though it's slow because there should be few, long-lived subscriptons
      await EnsureSubscription(topicName, subscriptionName);

      return SubscriptionClient.CreateFromConnectionString(_connString, topicName, subscriptionName);
    }

    private static async Task EnsureSubscription(string topicName, string subscriptionName) {
      if (!await _namespaceManager.SubscriptionExistsAsync(topicName, subscriptionName)) {
        await _namespaceManager.CreateSubscriptionAsync(new SubscriptionDescription(topicName, subscriptionName) {
          AutoDeleteOnIdle = TimeSpan.FromMinutes(5), // Minimum
          EnableBatchedOperations = true,
        });
      }
    }

    public static async Task EnsureQueues() {
      var checks = ShipHubQueueNames.AllQueues
        .Select(x => new { QueueName = x, ExistsTask = _namespaceManager.QueueExistsAsync(x) })
        .ToArray();

      await Task.WhenAll(checks.Select(x => x.ExistsTask));

      var creations = checks
        .Where(x => !x.ExistsTask.Result)
        .Select(x => _namespaceManager.CreateQueueAsync(new QueueDescription(x.QueueName) {
          //DefaultMessageTimeToLive = TimeSpan.FromMinutes(5),
          DefaultMessageTimeToLive = TimeSpan.FromDays(7),

          EnableExpress = true,
          EnableBatchedOperations = true,
          EnableDeadLetteringOnMessageExpiration = true,
          EnablePartitioning = true,
          IsAnonymousAccessible = false,
          MaxDeliveryCount = 10,
          MaxSizeInMegabytes = 5120,
        }));

      await Task.WhenAll(creations);
    }

    public static async Task EnsureTopics() {
      var checks = ShipHubTopicNames.AllTopics
        .Select(x => new { TopicName = x, ExistsTask = _namespaceManager.TopicExistsAsync(x) })
        .ToArray();

      await Task.WhenAll(checks.Select(x => x.ExistsTask));

      // Ensure AutoDeleteOnIdle IS NOT SET on the topic. Only subscriptions should delete.
      var creations = checks
        .Where(x => !x.ExistsTask.Result)
        .Select(x => _namespaceManager.CreateTopicAsync(new TopicDescription(x.TopicName) {
          DefaultMessageTimeToLive = TimeSpan.FromMinutes(5),
          EnableExpress = true,
          EnableBatchedOperations = true,
          EnablePartitioning = true,
          IsAnonymousAccessible = false,
          MaxSizeInMegabytes = 5120,
        }));

      await Task.WhenAll(creations);
    }

    public async Task NotifyChanges(IChangeSummary changeSummary) {
      var topic = TopicClientForName(ShipHubTopicNames.Changes);
      using (var bm = WebJobInterop.CreateMessage(new ChangeMessage(changeSummary))) {
        await topic.SendAsync(bm);
      }
    }

    public async Task SyncAccount(string accessToken) {
      var queue = QueueClientForName(ShipHubQueueNames.SyncAccount);

      var message = new AccessTokenMessage(accessToken);

      using (var bm = WebJobInterop.CreateMessage(message)) {
        await queue.SendAsync(bm);
      }
    }

    public async Task SyncAccountRepositories(long accountId, string login, string accessToken) {
      var message = new AccountMessage(accountId, login, accessToken);

      var queue = QueueClientForName(ShipHubQueueNames.SyncAccountRepositories);
      using (var bm = WebJobInterop.CreateMessage(message)) {
        await queue.SendAsync(bm);
      }
    }

    public async Task SyncRepositoryIssueTimeline(string accessToken, string repositoryFullName, int issueNumber) {
      var queue = QueueClientForName(ShipHubQueueNames.SyncRepositoryIssueTimeline);

      var message = new IssueMessage(repositoryFullName, issueNumber, accessToken);

      using (var bm = WebJobInterop.CreateMessage(message)) {
        await queue.SendAsync(bm);
      }
    }
  }
}
