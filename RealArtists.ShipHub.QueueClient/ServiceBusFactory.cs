namespace RealArtists.ShipHub.QueueClient {
  using System;
  using System.Collections.Concurrent;
  using System.Linq;
  using System.Threading.Tasks;
  using Common;
  using Microsoft.Azure;
  using Microsoft.ServiceBus;
  using Microsoft.ServiceBus.Messaging;

  public interface IServiceBusFactory {
    MessagingFactory MessagingFactory { get; }
    NamespaceManager NamespaceManager { get; }
    bool Paired { get; }

    Task<MessageSender> MessageSenderForName(string queueName);
    Task<SubscriptionClient> SubscriptionClientForName(string topicName, string subscriptionName = null);
  }

  public class ServiceBusFactory : IServiceBusFactory {
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMinutes(2);

    const string ConnectionStringKey = "AzureWebJobsServiceBus";
    const string PairConnectionStringKey = "AzureWebJobsServiceBusPair";

    bool _initialized;
    string _connString;
    string _pairConnString;

    public bool Paired { get; private set; }
    public NamespaceManager NamespaceManager { get; private set; }
    public MessagingFactory MessagingFactory { get; private set; }

    public ServiceBusFactory() :
      this(CloudConfigurationManager.GetSetting(ConnectionStringKey), CloudConfigurationManager.GetSetting(PairConnectionStringKey)) { }

    public ServiceBusFactory(string connectionString) : this(connectionString, null) { }

    public ServiceBusFactory(string connectionString, string pairConnectionString) {
      _connString = connectionString;
      _pairConnString = pairConnectionString;
    }

    public async Task Initialize() {
      if (_initialized) {
        throw new InvalidOperationException("Object is already initialized.");
      }

      NamespaceManager = NamespaceManager.CreateFromConnectionString(_connString);
      MessagingFactory = MessagingFactory.CreateFromConnectionString(_connString);

      // Create topics and queues
      await Task.WhenAll(EnsureQueues(), EnsureTopics());

      if (!_pairConnString.IsNullOrWhiteSpace()) {
        var pairManager = NamespaceManager.CreateFromConnectionString(_pairConnString);
        var pairFactory = MessagingFactory.CreateFromConnectionString(_pairConnString);

        var pairOpts = new SendAvailabilityPairedNamespaceOptions(pairManager, pairFactory, 10, TimeSpan.FromMinutes(1), true);

        // Look at pairOpts defaults and message factory retry options

        await MessagingFactory.PairNamespaceAsync(pairOpts);

        Paired = true;
      }

      _initialized = true;
    }

    //
    // Client creation and caching
    //

    ConcurrentDictionary<string, MessageSender> _messageSenders = new ConcurrentDictionary<string, MessageSender>();

    static async Task<T> CacheLookup<T>(ConcurrentDictionary<string, T> cache, string key, Func<Task<T>> valueCreator)
      where T : class {
      T client = null;
      if (!cache.TryGetValue(key, out client)) {
        client = await valueCreator();
        cache.TryAdd(key, client);
      }
      return client;
    }

    public async Task<MessageSender> MessageSenderForName(string queueName) {
      return await CacheLookup(_messageSenders, queueName, () => MessagingFactory.CreateMessageSenderAsync(queueName));
    }

    public async Task<SubscriptionClient> SubscriptionClientForName(string topicName, string subscriptionName = null) {
      // For auto expiring subscriptions we only care that the names never overlap
      if (subscriptionName.IsNullOrWhiteSpace()) {
        subscriptionName = Guid.NewGuid().ToString("N");
      }

      // ensure the subscription exists
      // safe to do this every time even though it's slow because there should be few, long-lived subscriptons
      await EnsureSubscription(topicName, subscriptionName);

      return MessagingFactory.CreateSubscriptionClient(topicName, subscriptionName);
    }

    //
    // Queue, Topic, and Subscription Maintenance
    //

    private async Task EnsureSubscription(string topicName, string subscriptionName) {
      if (!await NamespaceManager.SubscriptionExistsAsync(topicName, subscriptionName)) {
        await NamespaceManager.CreateSubscriptionAsync(new SubscriptionDescription(topicName, subscriptionName) {
          AutoDeleteOnIdle = TimeSpan.FromMinutes(5), // Minimum
          EnableBatchedOperations = true,
        });
      }
    }

    private async Task EnsureQueues() {
      var checks = ShipHubQueueNames.AllQueues
        .Select(x => new { QueueName = x, ExistsTask = NamespaceManager.QueueExistsAsync(x) })
        .ToArray();

      await Task.WhenAll(checks.Select(x => x.ExistsTask));

      var creations = checks
        .Where(x => !x.ExistsTask.Result)
        .Select(x => NamespaceManager.CreateQueueAsync(new QueueDescription(x.QueueName) {
          DefaultMessageTimeToLive = DefaultTimeToLive, // If we ever get that far behind start shedding.
          DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(10),
          EnableExpress = true,
          EnableBatchedOperations = true,
          EnableDeadLetteringOnMessageExpiration = true, // So we know when we've dropped events, and how many.
          EnablePartitioning = true,
          IsAnonymousAccessible = false,
          MaxDeliveryCount = 2, // Prevent explosions of errors.
          MaxSizeInMegabytes = 5120,
          RequiresDuplicateDetection = false,
        }));

      await Task.WhenAll(creations);
    }

    private async Task EnsureTopics() {
      var checks = ShipHubTopicNames.AllTopics
        .Select(x => new { TopicName = x, ExistsTask = NamespaceManager.TopicExistsAsync(x) })
        .ToArray();

      await Task.WhenAll(checks.Select(x => x.ExistsTask));

      // Ensure AutoDeleteOnIdle IS NOT SET on the topic. Only subscriptions should delete.
      var creations = checks
        .Where(x => !x.ExistsTask.Result)
        .Select(x => NamespaceManager.CreateTopicAsync(new TopicDescription(x.TopicName) {
          DefaultMessageTimeToLive = DefaultTimeToLive,
          EnableExpress = true,
          EnableBatchedOperations = true,
          EnablePartitioning = true,
          IsAnonymousAccessible = false,
          MaxSizeInMegabytes = 5120,
        }));

      await Task.WhenAll(creations);
    }
  }
}
