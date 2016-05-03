namespace RealArtists.Ship.Server.QueueClient {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using Microsoft.Azure;
  using Microsoft.ServiceBus;
  using Microsoft.ServiceBus.Messaging;
  using ResourceUpdate;
  using ShipHub.Common.GitHub;
  using ShipHub.Common.GitHub.Models;

  public class ResourceUpdateClient {
    static readonly string _connString;
    static readonly QueueClient _account;
    static readonly QueueClient _repository;

    static ResourceUpdateClient() {
      _connString = CloudConfigurationManager.GetSetting("Microsoft.ServiceBus.ConnectionString");

      _account = QueueClient.CreateFromConnectionString(_connString, ResourceQueueNames.Account);
      //QueueClient.CreateFromConnectionString(sbConnString, ResourceQueueNames.Comment);
      //QueueClient.CreateFromConnectionString(sbConnString, ResourceQueueNames.Issue);
      //QueueClient.CreateFromConnectionString(sbConnString, ResourceQueueNames.IssueEvent);
      //QueueClient.CreateFromConnectionString(sbConnString, ResourceQueueNames.Milestone);
      //QueueClient.CreateFromConnectionString(sbConnString, ResourceQueueNames.RateLimit);
      _repository = QueueClient.CreateFromConnectionString(_connString, ResourceQueueNames.Repository);
      //QueueClient.CreateFromConnectionString(sbConnString, ResourceQueueNames.Webhook);
    }

    public static async Task EnsureQueues() {
      var nm = NamespaceManager.CreateFromConnectionString(_connString);

      var names = new[] {
        ResourceQueueNames.Account,
        //ResourceQueueNames.Comment,
        //ResourceQueueNames.Issue,
        //ResourceQueueNames.IssueEvent,
        //ResourceQueueNames.Milestone,
        //ResourceQueueNames.RateLimit,
        ResourceQueueNames.Repository,
        //ResourceQueueNames.Webhook,
      };

      var checks = names.ToDictionary(x => x, x => nm.QueueExistsAsync(x));

      await Task.WhenAll(checks.Values);

      var creations = checks
        .Where(x => !x.Value.Result)
        .Select(x => nm.CreateQueueAsync(new QueueDescription(x.Key) {
          //DefaultMessageTimeToLive = TimeSpan.FromMinutes(5),
          DefaultMessageTimeToLive = TimeSpan.FromDays(7),

          //DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(1),
          DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(10),

          EnableBatchedOperations = true,
          //EnableExpress = true, // Can't enable this and duplicate detection
          EnablePartitioning = true,
          IsAnonymousAccessible = false,
          MaxSizeInMegabytes = 5120,
          RequiresDuplicateDetection = true,
        }));

      await Task.WhenAll(creations);
    }

    public Task Send(Account account, GitHubCacheData cacheData = null) {
      var message = new UpdateMessage<Account>(account, cacheData);
      return _account.SendAsync(new BrokeredMessage(message) {
        MessageId = $"{account.Id}/{account.UpdatedAt.ToUnixTimeSeconds()}",
        PartitionKey = $"{account.Id}",
      });
    }

    //public Task SendBatch(IEnumerable<Account> accounts, CacheMetaData cacheMetaData = null, WebhookMetaData webhookMetaData = null) {
    //  var messages = accounts.Select(x => new BrokeredMessage(
    //    new AccountUpdateMessage() {
    //      CacheMetaData = cacheMetaData,
    //      WebhookMetaData = webhookMetaData,
    //      Value = x,
    //    }
    //  ));
    //  return _account.SendBatchAsync(messages);
    //}

    public Task Send(Repository repo, GitHubCacheData cacheData = null) {
      var message = new UpdateMessage<Repository>(repo, cacheData);
      return _repository.SendAsync(new BrokeredMessage(message) {
        MessageId = $"{repo.Id}/{repo.UpdatedAt.ToUnixTimeSeconds()}",
        PartitionKey = $"{repo.Id}",
      });
    }
  }
}
