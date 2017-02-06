namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading.Tasks;
  using ActorInterfaces;
  using Actors;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Microsoft.Azure.WebJobs;
  using Newtonsoft.Json;
  using Orleans;
  using QueueClient;
  using QueueClient.Messages;
  using Tracing;

  public class GitHubWebhookQueueHandler : LoggingHandlerBase {
    private IGrainFactory _grainFactory;
    private IMapper _mapper;

    public GitHubWebhookQueueHandler(IGrainFactory grainFactory, IMapper mapper, IDetailedExceptionLogger logger)
      : base(logger) {
      _grainFactory = grainFactory;
      _mapper = mapper;
    }

    private T ReadPayload<T>(Guid secret, string payloadString, byte[] expectedSignature) {
      using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret.ToString()))) {
        var computedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadString));
        // We're not worth launching a timing attack against.
        if (!expectedSignature.SequenceEqual(computedSignature)) {
          throw new ArgumentException("Invalid webhook signature.");
        }

        var payload = JsonConvert.DeserializeObject<T>(payloadString, GitHubSerialization.JsonSerializerSettings);
        return payload;
      }
    }

    public async Task ProcessEvent(
      [ServiceBusTrigger(ShipHubQueueNames.WebhooksEvent)] GitHubWebhookEventMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger,
      ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, null, message, async () => {
        ChangeSummary changeSummary = null;
        using (var context = new ShipHubContext()) {
          Hook hook = null;
          if (message.EntityType == "org") {
            hook = await context.Hooks.SingleOrDefaultAsync(x => x.OrganizationId == message.EntityId);
          } else if (message.EntityType == "repo") {
            hook = await context.Hooks.SingleOrDefaultAsync(x => x.RepositoryId == message.EntityId);
          } else {
            throw new ArgumentException("EntityType must be repo or org");
          }

          if (hook == null) {
            // I don't care anymore. This is GitHub's problem.
            // They should support unsubscribing from a hook with a special response code or body.
            // We may not even have credentials to remove the hook anymore.
            return;
          }

          switch (message.EventName) {
            case "issues": {
                var payload = ReadPayload<WebhookIssuePayload>(hook.Secret, message.Payload, message.Signature);
                switch (payload.Action) {
                  case "opened":
                  case "closed":
                  case "reopened":
                  case "edited":
                  case "labeled":
                  case "unlabeled":
                  case "assigned":
                  case "unassigned":
                  case "milestoned":
                  case "demilestoned":
                    changeSummary = await HandleIssues(context, payload);
                    break;
                }
                break;
              }
            case "issue_comment": {
                var payload = ReadPayload<WebhookIssuePayload>(hook.Secret, message.Payload, message.Signature);
                switch (payload.Action) {
                  case "created":
                  case "edited":
                  case "deleted":
                    changeSummary = await HandleIssueComment(context, payload);
                    break;
                }
                break;
              }
            case "milestone": {
                var payload = ReadPayload<WebhookIssuePayload>(hook.Secret, message.Payload, message.Signature);
                switch (payload.Action) {
                  case "closed":
                  case "created":
                  case "deleted":
                  case "edited":
                  case "opened":
                    changeSummary = await HandleMilestone(context, payload);
                    break;
                }
                break;
              }
            case "label": {
                var payload = ReadPayload<WebhookIssuePayload>(hook.Secret, message.Payload, message.Signature);
                switch (payload.Action) {
                  case "created":
                  case "edited":
                  case "deleted":
                    changeSummary = await HandleLabel(context, payload);
                    break;
                }
                break;
              }
            case "repository": {
                var payload = ReadPayload<WebhookIssuePayload>(hook.Secret, message.Payload, message.Signature);
                if (
                // Created events can only come from the org-level hook.
                payload.Action == "created" ||
                // We'll get deletion events from both the repo and org, but
                // we'll ignore the org one.
                (message.EntityType == "repo" && payload.Action == "deleted")) {
                  await HandleRepository(context, payload);
                }
                break;
              }
            case "push": {
                var payload = ReadPayload<WebhookPushPayload>(hook.Secret, message.Payload, message.Signature);
                await HandlePush(payload);
                break;
              }
            case "ping":
              ReadPayload<object>(hook.Secret, message.Payload, message.Signature); // read payload to validate signature
              break;
            default:
              throw new NotImplementedException($"Webhook event '{message.EventName}' is not handled. Either support it or don't subscribe to it.");
          }

          // Reset the ping count so this webhook won't get reaped.
          await context.BulkUpdateHooks(seen: new[] { hook.Id });
        }

        if (changeSummary != null && !changeSummary.IsEmpty) {
          await notifyChanges.AddAsync(new ChangeMessage(changeSummary));
        }
      });
    }

    private async Task<ChangeSummary> HandleIssueComment(ShipHubContext context, WebhookIssuePayload payload) {
      // Ensure the issue that owns this comment exists locally before we add the comment.
      var summary = await HandleIssues(context, payload);

      if (payload.Action == "deleted") {
        summary.UnionWith(await context.DeleteComments(new[] { payload.Comment.Id }));
      } else {
        summary.UnionWith(await context.BulkUpdateAccounts(
        DateTimeOffset.UtcNow,
        _mapper.Map<IEnumerable<AccountTableType>>(new[] { payload.Comment.User })));

        summary.UnionWith(await context.BulkUpdateComments(
          payload.Repository.Id,
          _mapper.Map<IEnumerable<CommentTableType>>(new[] { payload.Comment })));
      }

      return summary;
    }

    private async Task HandleRepository(ShipHubContext context, WebhookIssuePayload payload) {
      if (payload.Repository.Owner.Type == GitHubAccountType.Organization) {
        var users = await context.OrganizationAccounts
          .Where(x => x.OrganizationId == payload.Repository.Owner.Id)
          .Where(x => x.User.Token != null)
          .Select(x => x.User)
          .ToListAsync();

        await Task.WhenAll(
          users.Select(x => {
            var userActor = _grainFactory.GetGrain<IUserActor>(x.Id);
            return userActor.ForceSyncRepositories();
          })
        );
      } else {
        // TODO: This should also trigger a sync for contributors of a repo, but at
        // least this is more correct than what we have now.
        var owner = await context.Accounts.SingleOrDefaultAsync(x => x.Id == payload.Repository.Owner.Id);
        if (owner.Token != null) {
          var userActor = _grainFactory.GetGrain<IUserActor>(owner.Id);
          await userActor.ForceSyncRepositories();
        }
      }
    }

    private async Task<ChangeSummary> HandleIssues(ShipHubContext context, WebhookIssuePayload payload) {
      var summary = new ChangeSummary();
      if (payload.Issue.Milestone != null) {
        var milestone = _mapper.Map<MilestoneTableType>(payload.Issue.Milestone);
        var milestoneSummary = await context.BulkUpdateMilestones(
          payload.Repository.Id,
          new MilestoneTableType[] { milestone });
        summary.UnionWith(milestoneSummary);
      }

      var referencedAccounts = new List<Common.GitHub.Models.Account>();
      referencedAccounts.Add(payload.Issue.User);
      if (payload.Issue.Assignees != null) {
        referencedAccounts.AddRange(payload.Issue.Assignees);
      }
      if (payload.Issue.ClosedBy != null) {
        referencedAccounts.Add(payload.Issue.ClosedBy);
      }

      if (referencedAccounts.Count > 0) {
        var accountsMapped = _mapper.Map<IEnumerable<AccountTableType>>(referencedAccounts.Distinct(x => x.Id));
        summary.UnionWith(await context.BulkUpdateAccounts(DateTimeOffset.UtcNow, accountsMapped));
      }

      var issues = new List<Common.GitHub.Models.Issue> { payload.Issue };
      var issuesMapped = _mapper.Map<IEnumerable<IssueTableType>>(issues);

      var labels = payload.Issue.Labels?.Select(x => new LabelTableType() {
        Id = x.Id,
        Color = x.Color,
        Name = x.Name,
      });

      var assigneeMappings = payload.Issue.Assignees?.Select(x => new MappingTableType() {
        Item1 = payload.Issue.Id,
        Item2 = x.Id,
      });

      summary.UnionWith(await context.BulkUpdateLabels(payload.Repository.Id, labels));
      summary.UnionWith(await context.BulkUpdateIssues(
        payload.Repository.Id,
        issuesMapped,
        payload.Issue.Labels?.Select(x => new MappingTableType() { Item1 = payload.Issue.Id, Item2 = x.Id }),
        assigneeMappings));

      return summary;
    }

    private async Task<ChangeSummary> HandleMilestone(ShipHubContext context, WebhookIssuePayload payload) {
      if (payload.Action == "deleted") {
        var summary = await context.DeleteMilestone(payload.Milestone.Id);
        return summary;
      } else {
        var summary = await context.BulkUpdateMilestones(
          payload.Repository.Id,
          new[] { _mapper.Map<MilestoneTableType>(payload.Milestone) });
        return summary;
      }
    }

    private Task HandlePush(WebhookPushPayload payload) {
      var defaultBranch = payload.Repository.DefaultBranch ?? "";
      var defaultRef = $"refs/heads/{defaultBranch}";
      if (defaultRef != payload.Ref) {
        return Task.CompletedTask; // if not a push to default branch then we don't care
      }

      bool hasIssueTemplate;
      if (payload.Size > payload.Commits.Count()) {
        hasIssueTemplate = true; // if we can't enumerate all the commits, assume ISSUE_TEMPLATE was changed.
      } else {
        // the payload includes all of the commits. take a look and see if we can find the ISSUE_TEMPLATE.md in any of the edited files
        hasIssueTemplate = payload.Commits
          .Aggregate(new HashSet<string>(), (accum, commit) => {
            accum.UnionWith(commit.Added);
            accum.UnionWith(commit.Modified);
            accum.UnionWith(commit.Removed);
            return accum;
          })
          .Any(f => RepositoryActor.EndsWithIssueTemplateRegex.IsMatch(f));
      }

      if (hasIssueTemplate) {
        var repoActor = _grainFactory.GetGrain<IRepositoryActor>(payload.Repository.Id);
        return repoActor.SyncIssueTemplate();
      } else {
        return Task.CompletedTask;
      }
    }

    private async Task<ChangeSummary> HandleLabel(ShipHubContext context, WebhookIssuePayload payload) {
      switch (payload.Action) {
        case "created":
        case "edited":
          return await context.BulkUpdateLabels(
          payload.Repository.Id,
          new[] {
            new LabelTableType() {
              Id = payload.Label.Id,
              Name = payload.Label.Name,
              Color = payload.Label.Color,
            },
          });
        case "deleted":
          return await context.DeleteLabel(payload.Label.Id);
        default:
          throw new ArgumentException("Unexpected action: " + payload.Action);
      }
    }
  }
}
