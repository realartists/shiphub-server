namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Microsoft.Azure.WebJobs;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using QueueClient;
  using QueueClient.Messages;
  using Tracing;
  using gm = Common.GitHub.Models;

#if DEBUG
  using System.Diagnostics;
#endif

  public class SyncQueueHandler : LoggingHandlerBase {
    private IMapper _mapper;
    private IGrainFactory _grainFactory;

    public SyncQueueHandler(IDetailedExceptionLogger logger, IMapper mapper, IGrainFactory grainFactory) : base(logger) {
      _mapper = mapper;
      _grainFactory = grainFactory;
    }

    /// <summary>
    /// Precondition: Repository exists
    /// Postcondition: Milestones exist
    /// </summary>
    public async Task SyncRepositoryMilestones(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryMilestones)] TargetMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssues)] IAsyncCollector<TargetMessage> syncRepoIssues,
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
          var metadata = repo.MilestoneMetadata;

          logger.WriteLine($"Milestones for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository milestones.");
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Token);

            var response = await ghc.Milestones(repo.FullName, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var milestones = response.Result;

              changes = await context.BulkUpdateMilestones(repo.Id, _mapper.Map<IEnumerable<MilestoneTableType>>(milestones));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "MilestoneMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);

          // Sync Issues regardless
          await syncRepoIssues.AddAsync(message);
        }
      });
    }

    /// <summary>
    /// Precondition: Repository exists
    /// Postcondition: Labels exist
    /// </summary>
    public async Task SyncRepositoryLabels(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryLabels)] TargetMessage message,
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
          var metadata = repo.LabelMetadata;

          logger.WriteLine($"Labels for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository labels.");
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Token);

            var response = await ghc.Labels(repo.FullName, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var labels = response.Result;

              changes = await context.SetRepositoryLabels(
                repo.Id,
                labels.Select(x => new LabelTableType() {
                  ItemId = repo.Id,
                  Color = x.Color,
                  Name = x.Name
                })
              );

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "LabelMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    /// <summary>
    /// Precondition: Repository and Milestones exist
    /// Postcondition: Issues exist
    /// </summary>
    public async Task SyncRepositoryIssues(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssues)] TargetMessage message,
      //[ServiceBus(ShipHubQueueNames.SyncRepositoryComments)] IAsyncCollector<TargetMessage> syncRepoComments,
      //[ServiceBus(ShipHubQueueNames.SyncRepositoryIssueEvents)] IAsyncCollector<TargetMessage> syncRepoIssueEvents,
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
          var metadata = repo.IssueMetadata;

          logger.WriteLine($"Issues for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository issues.");
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Token);

            var response = await ghc.Issues(repo.FullName, null, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var issues = response.Result;

              var accounts = issues
                .SelectMany(x => new[] { x.User, x.ClosedBy }.Concat(x.Assignees))
                .Where(x => x != null)
                .Distinct(x => x.Id);
              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(accounts));

              var milestones = issues
                .Select(x => x.Milestone)
                .Where(x => x != null)
                .Distinct(x => x.Id);
              changes.UnionWith(await context.BulkUpdateMilestones(repo.Id, _mapper.Map<IEnumerable<MilestoneTableType>>(milestones)));

              changes.UnionWith(await context.BulkUpdateIssues(
                repo.Id,
                _mapper.Map<IEnumerable<IssueTableType>>(issues),
                issues.SelectMany(x => x.Labels?.Select(y => new LabelTableType() { ItemId = x.Id, Color = y.Color, Name = y.Name })),
                issues.SelectMany(x => x.Assignees?.Select(y => new MappingTableType() { Item1 = x.Id, Item2 = y.Id }))
              ));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "IssueMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);

          // Do these unconditionally
          //await Task.WhenAll(
          //  syncRepoComments.AddAsync(message),
          //  syncRepoIssueEvents.AddAsync(message)
          //);
        }
      });
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
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Token);

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
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Token);

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
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Token);

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
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Token);

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
            var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Token);

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

    private static HashSet<string> _IgnoreTimelineEvents = new HashSet<string>(new[] { "commented", "subscribed", "unsubscribed" }, StringComparer.OrdinalIgnoreCase);
    public async Task SyncRepositoryIssueTimeline(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueTimeline)] IssueViewMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueComments)] IAsyncCollector<TargetMessage> syncIssueComments,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueReactions)] IAsyncCollector<TargetMessage> syncRepoIssueReactions,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssues)] IAsyncCollector<TargetMessage> syncRepoIssues,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      ///////////////////////////////////////////
      /* NOTE!
       * We can't sync the timeline incrementally, because the client wants commit and
       * reference data inlined. This means we always have to download all the
       * timeline events in case an old one now has updated data. Other options are to
       * just be wrong, or to simply reference the user by id and mark them referenced
       * by the repo.
       */
      //////////////////////////////////////////
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = new ChangeSummary();

          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var ghc = _grainFactory.GetGrain<IGitHubActor>(user.Token);

          // Client doesn't send repoId :(
          var repoId = await context.Repositories
            .Where(x => x.FullName == message.RepositoryFullName)
            .Select(x => x.Id)
            .SingleAsync();

          // Look up the issue info
          var issueInfo = await context.Issues
            .Where(x => x.RepositoryId == repoId && x.Number == message.Number)
            .SingleOrDefaultAsync();

          var issueId = issueInfo?.Id;

          // Sadly there exist cases where the client knows about issues before the server.
          // Even hooks aren't fast enough to completely prevent this.
          // The intercepting proxy should alleviate most but not all cases.
          // In the meantime, always discover/refresh the issue on view
          var issueResponse = await ghc.Issue(message.RepositoryFullName, message.Number, issueInfo?.Metadata);
          if (issueResponse.Status != HttpStatusCode.NotModified) {
            var update = issueResponse.Result;

            issueId = update.Id;

            // TODO: Unify this code with other issue update places to reduce bugs.

            var upAccounts = new[] { update.User, update.ClosedBy }.Concat(update.Assignees)
                .Where(x => x != null)
                .Distinct(x => x.Id);
            changes.UnionWith(await context.BulkUpdateAccounts(issueResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(upAccounts)));

            if (update.Milestone != null) {
              changes.UnionWith(await context.BulkUpdateMilestones(repoId, _mapper.Map<IEnumerable<MilestoneTableType>>(new[] { update.Milestone })));
            }

            changes.UnionWith(await context.BulkUpdateIssues(
              repoId,
              _mapper.Map<IEnumerable<IssueTableType>>(new[] { update }),
              update.Labels?.Select(y => new LabelTableType() { ItemId = update.Id, Color = y.Color, Name = y.Name }),
              update.Assignees?.Select(y => new MappingTableType() { Item1 = update.Id, Item2 = y.Id })
            ));
          }

          tasks.Add(context.UpdateMetadata("Issues", "MetadataJson", issueId.Value, issueResponse));

          // This will be cached per-user by the ShipHubFilter.
          var timelineResponse = await ghc.Timeline(message.RepositoryFullName, message.Number);
          if (timelineResponse.Status != HttpStatusCode.NotModified) {
            var timeline = timelineResponse.Result;

            // Now just filter
            var filteredEvents = timeline.Where(x => !_IgnoreTimelineEvents.Contains(x.Event)).ToArray();

            // For adding to the DB later
            var accounts = new List<gm.Account>();

            foreach (var tl in filteredEvents) {
              accounts.Add(tl.Actor);
              accounts.Add(tl.Assignee);
              accounts.Add(tl.Source?.Actor);
            }

            // Find all events with associated commits, and embed them.
            var withCommits = filteredEvents.Where(x => !x.CommitUrl.IsNullOrWhiteSpace()).ToArray();
            var commits = withCommits.Select(x => x.CommitUrl).Distinct();

            if (commits.Any()) {
              var commitLookups = commits
                .Select(x => {
                  var parts = x.Split('/');
                  var numParts = parts.Length;
                  var repoName = parts[numParts - 4] + "/" + parts[numParts - 3];
                  var sha = parts[numParts - 1];
                  return new {
                    Id = x,
                    Task = ghc.Commit(repoName, sha, GitHubCacheDetails.Empty),
                  };
                })
                .ToDictionary(x => x.Id, x => x.Task);

              // TODO: Lookup Repo Name->ID mapping

              await Task.WhenAll(commitLookups.Values);

              foreach (var item in withCommits) {
                var lookup = commitLookups[item.CommitUrl].Result;

                // best effort - requests will fail when the user doesn't have source access.
                // see Nick's account and references from the github-beta repo
                if (lookup.IsError) {
                  continue;
                }

                var commit = lookup.Result;
                accounts.Add(commit.Author);
                accounts.Add(commit.Committer);
                item.ExtensionDataDictionary["ship_commit_message"] = commit.CommitDetails.Message;
                if (commit.Author != null) {
                  item.ExtensionDataDictionary["ship_commit_author"] = JObject.FromObject(commit.Author);
                }
                if (commit.Committer != null) {
                  item.ExtensionDataDictionary["ship_commit_committer"] = JObject.FromObject(commit.Committer);
                }
              }
            }

            var withSources = filteredEvents.Where(x => x.Source != null).ToArray();
            var sources = withSources.Select(x => x.Source.IssueUrl).Distinct();

            if (sources.Any()) {
              var sourceLookups = sources
                .Select(x => {
                  var parts = x.Split('/');
                  var numParts = parts.Length;
                  var repoName = parts[numParts - 4] + "/" + parts[numParts - 3];
                  var issueNum = int.Parse(parts[numParts - 1]);
                  return new {
                    Id = x,
                    Task = ghc.Issue(repoName, issueNum, GitHubCacheDetails.Empty),
                  };
                })
                .ToDictionary(x => x.Id, x => x.Task);

              await Task.WhenAll(sourceLookups.Values);

              var prLookups = sourceLookups.Values
                .Where(x => x.Result.Result.PullRequest != null)
                .Select(x => {
                  var url = x.Result.Result.PullRequest.Url;
                  var parts = url.Split('/');
                  var numParts = parts.Length;
                  var repoName = parts[numParts - 4] + "/" + parts[numParts - 3];
                  var prNum = int.Parse(parts[numParts - 1]);
                  return new {
                    Id = url,
                    Task = ghc.PullRequest(repoName, prNum, GitHubCacheDetails.Empty),
                  };
                })
                .ToDictionary(x => x.Id, x => x.Task);

              await Task.WhenAll(prLookups.Values);

              foreach (var item in withSources) {
                var refIssue = sourceLookups[item.Source.IssueUrl].Result.Result;
                accounts.Add(item.Source.Actor);
                if (refIssue.Assignees.Any()) {
                  accounts.AddRange(refIssue.Assignees); // Do we need both assignee and assignees? I think yes.
                }
                accounts.Add(refIssue.ClosedBy);
                accounts.Add(refIssue.User);

                item.ExtensionDataDictionary["ship_issue_state"] = refIssue.State;
                item.ExtensionDataDictionary["ship_issue_title"] = refIssue.Title;

                if (refIssue.PullRequest != null) {
                  item.ExtensionDataDictionary["ship_is_pull_request"] = true;

                  var pr = prLookups[refIssue.PullRequest.Url].Result.Result;
                  item.ExtensionDataDictionary["ship_pull_request_merged"] = pr.Merged;
                }
              }
            }

            // Update accounts
            var uniqueAccounts = accounts
              .Where(x => x != null)
              .Distinct(x => x.Login);
            var accountsParam = _mapper.Map<IEnumerable<AccountTableType>>(uniqueAccounts);
            changes.UnionWith(await context.BulkUpdateAccounts(timelineResponse.Date, accountsParam));

            var issueMessage = new TargetMessage(issueId.Value, user.Id);

            // Now safe to sync reactions
            tasks.Add(syncRepoIssueReactions.AddAsync(issueMessage));

            // If we find comments, sync them
            // TODO: Incrementally
            if (timeline.Any(x => x.Event == "commented")) {
              tasks.Add(syncIssueComments.AddAsync(issueMessage));
              // Can't sync comment reactions yet in case they don't exist
            }

            // Cleanup the data
            foreach (var item in filteredEvents) {
              // Oh GitHub, how I hate thee. Why can't you provide ids?
              // We're regularly seeing GitHub ids as large as 31 bits.
              // We can only store four things this way because we only have two free bits :(
              // TODO: HACK! THIS IS BRITTLE AND WILL BREAK!
              var ones31 = 0x7FFFFFFFL;
              var issuePart = (issueId.Value & ones31);
              if (issuePart != issueId.Value) {
                throw new NotSupportedException($"IssueId {issueId.Value} exceeds 31 bits!");
              }
              switch (item.Event) {
                case "cross-referenced":
                  // high bits 11
                  var commentPart = (item.Source.CommentId & ones31);
                  if (commentPart != item.Source.CommentId) {
                    throw new NotSupportedException($"CommentId {item.Source.CommentId} exceeds 31 bits!");
                  }
                  item.Id = ((long)3) << 62 | commentPart << 31 | issuePart;
                  item.Actor = item.Source.Actor;
                  break;
                case "committed":
                  // high bits 10
                  var sha = item.ExtensionDataDictionary["sha"].ToObject<string>();
                  var shaBytes = SoapHexBinary.Parse(sha).Value;
                  var shaPart = BitConverter.ToInt64(shaBytes, 0) & ones31;
                  item.Id = ((long)2) << 62 | shaPart << 31 | issuePart;
                  item.CreatedAt = item.ExtensionDataDictionary["committer"]["date"].ToObject<DateTimeOffset>();
                  break;
                default:
                  break;
              }

#if DEBUG
              // Sanity check whilst debugging
              if (item.Id == 0
                || item.CreatedAt == DateTimeOffset.MinValue) {
                // Ruh roh
                Debugger.Break();
              }
#endif
            }

            // This conversion handles the restriction field and hash.
            var events = _mapper.Map<IEnumerable<IssueEventTableType>>(filteredEvents);

            // Set issueId
            foreach (var item in events) {
              item.IssueId = issueId.Value;
            }
            changes.UnionWith(await context.BulkUpdateTimelineEvents(user.Id, repoId, events, accountsParam.Select(x => x.Id)));
          }

          tasks.Add(notifyChanges.Send(changes));
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
