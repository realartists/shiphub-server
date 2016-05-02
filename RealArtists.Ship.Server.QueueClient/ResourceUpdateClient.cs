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
          // TODO: Duplicate detection and other settings.
        }));

      await Task.WhenAll(creations);
    }

    public Task Send(Account account, CacheMetaData cacheMetaData = null, WebhookMetaData webhookMetaData = null) {
      var message = new AccountUpdateMessage() {
        CacheMetaData = cacheMetaData,
        WebhookMetaData = webhookMetaData,
        Value = account,
      };
      return _account.SendAsync(new BrokeredMessage(message));
    }

    public Task Send(Repository repo, CacheMetaData cacheMetaData = null, WebhookMetaData webhookMetaData = null) {
      var message = new RepositoryUpdateMessage() {
        CacheMetaData = cacheMetaData,
        WebhookMetaData = webhookMetaData,
        Value = repo,
        AccountId = repo.Owner.Id,
      };
      return _repository.SendAsync(new BrokeredMessage(message));
    }
  }
}
