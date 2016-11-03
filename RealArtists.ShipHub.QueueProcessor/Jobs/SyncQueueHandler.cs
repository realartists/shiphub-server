namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Microsoft.Azure.WebJobs;
  using Orleans;
  using QueueClient;
  using QueueClient.Messages;
  using Tracing;

  public class SyncQueueHandler : LoggingHandlerBase {
    private IMapper _mapper;
    private IGrainFactory _grainFactory;

    public SyncQueueHandler(IDetailedExceptionLogger logger, IMapper mapper, IGrainFactory grainFactory) : base(logger) {
      _mapper = mapper;
      _grainFactory = grainFactory;
    }

    public async Task SyncRepositoryComments(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryComments)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var repo = await context.Repositories.SingleAsync(x => x.Id == message.TargetId);
          var metadata = repo.CommentMetadata;

          logger.WriteLine($"Comments for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository comments.");
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Id);

            var response = await ghc.Comments(repo.FullName, null, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var comments = response.Result;

              var users = comments
                .Select(x => x.User)
                .Distinct(x => x.Id);
              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(users));

              var issueComments = comments.Where(x => x.IssueNumber != null);
              changes.UnionWith(await context.BulkUpdateComments(repo.Id, _mapper.Map<IEnumerable<CommentTableType>>(issueComments)));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "CommentMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    public async Task SyncRepositoryIssueEvents(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueEvents)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var repo = await context.Repositories.SingleAsync(x => x.Id == message.TargetId);
          var metadata = repo.EventMetadata;

          logger.WriteLine($"Events for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository events.");
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Id);

            // TODO: Cute pagination trick to detect latest only.
            var response = await ghc.Events(repo.FullName, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var events = response.Result;

              // For now only grab accounts from the response.
              // Sometimes an issue is also included, but not always, and we get them elsewhere anyway.
              var accounts = events
                .SelectMany(x => new[] { x.Actor, x.Assignee, x.Assigner })
                .Where(x => x != null)
                .Distinct(x => x.Login);
              var accountsParam = _mapper.Map<IEnumerable<AccountTableType>>(accounts);
              changes = await context.BulkUpdateAccounts(response.Date, accountsParam);
              var eventsParam = _mapper.Map<IEnumerable<IssueEventTableType>>(events);
              changes.UnionWith(await context.BulkUpdateIssueEvents(user.Id, repo.Id, eventsParam, accountsParam.Select(x => x.Id)));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "EventMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    public async Task SyncRepositoryIssueComments(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueComments)] TargetMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueCommentReactions)] IAsyncCollector<TargetMessage> syncReactions,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var issue = await context.Issues
            .Include(x => x.Repository)
            .SingleAsync(x => x.Id == message.TargetId);
          var metadata = issue.CommentMetadata;

          logger.WriteLine($"Comments for {issue.Repository.FullName}#{issue.Number} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Issue comments.");
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Id);

            // TODO: Cute pagination trick to detect latest only.
            var response = await ghc.Comments(issue.Repository.FullName, null, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var comments = response.Result;

              var users = comments
                .Select(x => x.User)
                .Distinct(x => x.Id);
              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(users));

              foreach (var comment in comments) {
                if (comment.IssueNumber == null) {
                  comment.IssueNumber = issue.Number;
                }
              }

              changes.UnionWith(await context.BulkUpdateComments(
                issue.Repository.Id,
                _mapper.Map<IEnumerable<CommentTableType>>(comments)));

              tasks.Add(notifyChanges.Send(changes));

              tasks.AddRange(comments.Select(x => syncReactions.AddAsync(new TargetMessage(x.Id, message.ForUserId))));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Issues", "CommentMetadataJson", issue.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    public async Task SyncRepositoryIssueReactions(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueReactions)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var issue = await context.Issues
            .Include(x => x.Repository)
            .SingleAsync(x => x.Id == message.TargetId);
          var metadata = issue.ReactionMetadata;

          logger.WriteLine($"Reactions for {issue.Repository.FullName}#{issue.Number} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Issue reactions.");
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Id);

            var response = await ghc.IssueReactions(issue.Repository.FullName, issue.Number, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var reactions = response.Result;

              var users = reactions
                .Select(x => x.User)
                .Distinct(x => x.Id);
              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(users));

              changes.UnionWith(await context.BulkUpdateIssueReactions(
                issue.RepositoryId,
                issue.Id,
                _mapper.Map<IEnumerable<ReactionTableType>>(reactions)));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Issues", "ReactionMetadataJson", issue.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    public async Task SyncRepositoryIssueCommentReactions(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueCommentReactions)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var comment = await context.Comments
            .Include(x => x.Repository)
            .SingleAsync(x => x.Id == message.TargetId);
          var metadata = comment.ReactionMetadata;

          logger.WriteLine($"Reactions for comment {comment.Id} in {comment.Repository.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Issue reactions.");
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Id);

            var response = await ghc.IssueCommentReactions(comment.Repository.FullName, comment.Id, metadata);
            switch (response.Status) {
              case HttpStatusCode.NotModified:
                logger.WriteLine("Github: Not modified.");
                break;
              case HttpStatusCode.NotFound:
                // Deleted
                changes = await context.DeleteComments(new[] { comment.Id });
                break;
              default:
                logger.WriteLine("Github: Changed. Saving changes.");
                var reactions = response.Result;

                var users = reactions
                  .Select(x => x.User)
                  .Distinct(x => x.Id);
                changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(users));

                changes.UnionWith(await context.BulkUpdateCommentReactions(
                  comment.RepositoryId,
                  comment.Id,
                  _mapper.Map<IEnumerable<ReactionTableType>>(reactions)));
                break;
            }

            if (changes != null && !changes.Empty) {
              tasks.Add(notifyChanges.Send(changes));
            }

            tasks.Add(context.UpdateMetadata("Comments", "ReactionMetadataJson", comment.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }
  }

  public static class SyncHandlerExtensions {
    public static Task Send(this IAsyncCollector<ChangeMessage> topic, ChangeSummary summary) {
      if (summary != null && !summary.Empty) {
        return topic.AddAsync(new ChangeMessage(summary));
      }
      return Task.CompletedTask;
    }
  }
}
