namespace RealArtists.ShipHub.QueueClient {
  using System;
  using System.Collections.Concurrent;
  using System.Configuration;
  using System.Linq;
  using System.Threading.Tasks;
  using Messages;
  using Microsoft.ServiceBus;
  using Microsoft.ServiceBus.Messaging;

  public class ShipHubBusClient : IShipHubBusClient {
    static readonly string _connString;
    static readonly NamespaceManager _namespaceManager;
    static ConcurrentDictionary<string, QueueClient> _queueClients = new ConcurrentDictionary<string, QueueClient>();
    static ConcurrentDictionary<string, TopicClient> _topicClients = new ConcurrentDictionary<string, TopicClient>();
    static ConcurrentDictionary<string, SubscriptionClient> _subscriptionClients = new ConcurrentDictionary<string, SubscriptionClient>();

    static ShipHubBusClient() {
      _connString = ConfigurationManager.ConnectionStrings["AzureWebJobsServiceBus"].ConnectionString;
      _namespaceManager = NamespaceManager.CreateFromConnectionString(_connString);
    }

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
    public static SubscriptionClient SubscriptionClientForName(string topicName, string subscriptionName) {
      return CacheLookup(_subscriptionClients, topicName, () => SubscriptionClient.CreateFromConnectionString(_connString, topicName, subscriptionName));
    }

    // TODO: Roll into client creation
    public static async Task EnsureSubscription(string topicName, string subscriptionName) {
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

    public Task SyncAccount(string accessToken) {
      var queue = QueueClientForName(ShipHubQueueNames.SyncAccount);
      var message = new AccessTokenMessage() {
        AccessToken = accessToken,
      };
      return queue.SendAsync(WebJobInterop.CreateMessage(message));
    }
  }
}
