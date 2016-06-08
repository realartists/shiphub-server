namespace RealArtists.ShipHub.QueueClient {
  using System;
  using System.Collections.Concurrent;
  using System.Configuration;
  using System.Linq;
  using System.Threading.Tasks;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Messages;
  using Microsoft.Azure;
  using Microsoft.ServiceBus;
  using Microsoft.ServiceBus.Messaging;

  public class ShipHubQueueClient {
    static readonly string _connString;
    static ConcurrentDictionary<string, QueueClient> _queueClients = new ConcurrentDictionary<string, QueueClient>();

    static ShipHubQueueClient() {
      _connString = ConfigurationManager.ConnectionStrings["AzureWebJobsServiceBus"].ConnectionString;
    }

    static QueueClient QueueClientForName(string queueName) {
      QueueClient client = null;
      if (!_queueClients.TryGetValue(queueName, out client)) {
        client = QueueClient.CreateFromConnectionString(_connString, queueName);
        _queueClients.TryAdd(queueName, client);
      }
      return client;
    }

    public static async Task EnsureQueues() {
      var nm = NamespaceManager.CreateFromConnectionString(_connString);
      var checks = ShipHubQueueNames.AllQueues
        .Select(x => new { QueueName = x, ExistsTask = nm.QueueExistsAsync(x) })
        .ToArray();

      await Task.WhenAll(checks.Select(x => x.ExistsTask));

      var creations = checks
        .Where(x => !x.ExistsTask.Result)
        .Select(x => nm.CreateQueueAsync(new QueueDescription(x.QueueName) {
          //DefaultMessageTimeToLive = TimeSpan.FromMinutes(5),
          DefaultMessageTimeToLive = TimeSpan.FromDays(7),

          //RequiresDuplicateDetection = true,
          EnableExpress = true, // Can't enable this and duplicate detection

          //DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(1),
          //DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(10),

          EnableBatchedOperations = true,
          EnablePartitioning = true,
          IsAnonymousAccessible = false,
          MaxSizeInMegabytes = 5120,
        }));

      await Task.WhenAll(creations);
    }

    //public Task UpdateAccount(Account account, DateTimeOffset responseDate) {
    //  var queue = QueueClientForName(ShipHubQueueNames.UpdateAccount);
    //  return queue.SendAsync(WebJobInterop.CreateMessage(
    //    new UpdateMessage<Account>(account, responseDate),
    //    partitionKey: $"{account.Id}")
    //  );
    //}

    //public Task UpdateRepository(Repository repo, DateTimeOffset responseDate, GitHubCacheData cacheData = null) {
    //  var queue = QueueClientForName(ShipHubQueueNames.UpdateRepository);
    //  return queue.SendAsync(WebJobInterop.CreateMessage(
    //    new UpdateMessage<Repository>(repo, responseDate, cacheData),
    //    partitionKey: $"{repo.Id}")
    //  );
    //}

    public Task SyncAccount(string accessToken) {
      var queue = QueueClientForName(ShipHubQueueNames.SyncAccount);
      var message = new AccessTokenMessage() {
        AccessToken = accessToken,
      };
      return queue.SendAsync(WebJobInterop.CreateMessage(message));
    }
  }
}
