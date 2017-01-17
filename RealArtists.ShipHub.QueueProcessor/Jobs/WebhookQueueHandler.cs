namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Microsoft.Azure.WebJobs;
  using Orleans;
  using QueueClient;
  using QueueClient.Messages;
  using Tracing;
  using gm = Common.GitHub.Models;

  public class WebhookQueueHandler : LoggingHandlerBase {
    public static ISet<string> RequiredEvents { get; } = new HashSet<string>() {
      "issues",
      "issue_comment",
      "label",
      "milestone",
      "push"
    };

    private IShipHubConfiguration _configuration;
    private IGrainFactory _grainFactory;

    public WebhookQueueHandler(IShipHubConfiguration configuration, IGrainFactory grainFactory, IDetailedExceptionLogger logger)
      : base(logger) {
      _configuration = configuration;
      _grainFactory = grainFactory;
    }

    [Singleton("{TargetId}")]
    public async Task AddOrUpdateRepoWebhooks(
      [ServiceBusTrigger(ShipHubQueueNames.AddOrUpdateRepoWebhooks)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        var gh = _grainFactory.GetGrain<IGitHubActor>(message.ForUserId);
        await AddOrUpdateRepoWebhooksWithClient(message, gh, notifyChanges);
      });
    }

    public async Task AddOrUpdateRepoWebhooksWithClient(
      TargetMessage message,
      IGitHubActor client,
      IAsyncCollector<ChangeMessage> notifyChanges) {
      var apiHostName = _configuration.ApiHostName;
      Repository repo;
      Hook hook;
      using (var context = new ShipHubContext()) {
        repo = await context.Repositories.AsNoTracking().SingleAsync(x => x.Id == message.TargetId);

        if (repo.Disabled) {
          // all requests to it will fail
          Trace.TraceWarning($"Skipping disabled repo {repo.FullName}");
          return;
        }

        hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.RepositoryId == message.TargetId);

        if (hook != null && hook.GitHubId == null) {
          // We attempted to add a webhook for this earlier, but something failed
          // and we never got a chance to learn its GitHubId.
          await context.BulkUpdateHooks(deleted: new[] { hook.Id });
          hook = null;
        }
      }

      // This new connection is efficient because only stored procedures are used from here on.
      using (var context = new ShipHubContext()) {
        if (hook == null) {
          var hookList = await client.RepositoryWebhooks(repo.FullName, GitHubCacheDetails.Empty);
          if (!hookList.IsOk) {
            // webhooks are best effort
            // this keeps us from spewing errors and retrying a ton when an org is unpaid
            Trace.TraceWarning($"Unable to list hooks for {repo.FullName}");
            return;
          }

          var existingHooks = hookList.Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{apiHostName}/", StringComparison.OrdinalIgnoreCase));

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in existingHooks) {
            var deleteResponse = await client.DeleteRepositoryWebhook(repo.FullName, existingHook.Id);
            if (!deleteResponse.Succeeded) {
              Trace.TraceWarning($"Failed to delete existing hook ({existingHook.Id}) for repo '{repo.FullName}'");
            }
          }

          // GitHub will immediately send a ping when the webhook is created.
          // To avoid any chance for a race, add the Hook to the DB first, then
          // create on GitHub.
          var newHook = await context.CreateHook(Guid.NewGuid(), string.Join(",", RequiredEvents), repositoryId: repo.Id);

          bool deleteHook = false;
          try {
            var addRepoHookResponse = await client.AddRepositoryWebhook(
              repo.FullName,
              new gm.Webhook() {
                Name = "web",
                Active = true,
                Events = RequiredEvents,
                Config = new gm.WebhookConfiguration() {
                  Url = $"https://{apiHostName}/webhook/repo/{repo.Id}",
                  ContentType = "json",
                  Secret = newHook.Secret.ToString(),
                },
              });

            if (addRepoHookResponse.Succeeded) {
              newHook.GitHubId = addRepoHookResponse.Result.Id;
              var changeSummary = await context.BulkUpdateHooks(hooks: new[] { newHook });
              await notifyChanges.AddAsync(new ChangeMessage(changeSummary));
            } else {
              Trace.TraceWarning($"Failed to add hook for repo '{repo.FullName}' ({repo.Id}): {addRepoHookResponse.Status} {addRepoHookResponse.Error?.ToException()}");
              deleteHook = true;
            }
          } catch (Exception e) {
            e.Report($"Failed to add hook for repo '{repo.FullName}' ({repo.Id})");
            deleteHook = true;
            throw;
          } finally {
            if (deleteHook) {
              await context.BulkUpdateHooks(deleted: new[] { newHook.Id });
            }
          }
        } else if (!hook.Events.Split(',').ToHashSet().SetEquals(RequiredEvents)) {
          var editResponse = await client.EditRepositoryWebhookEvents(repo.FullName, (long)hook.GitHubId, RequiredEvents);

          if (!editResponse.Succeeded) {
            Trace.TraceWarning($"Failed to edit hook for repo '{repo.FullName}' ({repo.Id}): {editResponse.Status} {editResponse.Error?.ToException()}");
          } else {
            await context.BulkUpdateHooks(hooks: new[] {
              new HookTableType(){
                Id = hook.Id,
                GitHubId = editResponse.Result.Id,
                Secret = hook.Secret,
                Events = string.Join(",", editResponse.Result.Events),
              }
            });
          }
        }
      }
    }

    [Singleton("{TargetId}")]
    public async Task AddOrUpdateOrgWebhooks(
      [ServiceBusTrigger(ShipHubQueueNames.AddOrUpdateOrgWebhooks)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        var gh = _grainFactory.GetGrain<IGitHubActor>(message.ForUserId);
        await AddOrUpdateOrgWebhooksWithClient(message, gh, notifyChanges);
      });
    }

    public async Task AddOrUpdateOrgWebhooksWithClient(
      TargetMessage message,
      IGitHubActor client,
      IAsyncCollector<ChangeMessage> notifyChanges) {
      var apiHostName = _configuration.ApiHostName;
      var requiredEvents = new string[] { "repository", };

      Organization org;
      Hook hook;
      using (var context = new ShipHubContext()) {
        org = await context.Organizations.AsNoTracking().SingleAsync(x => x.Id == message.TargetId);

        hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == message.TargetId);

        if (hook != null && hook.GitHubId == null) {
          // We attempted to add a webhook for this earlier, but something failed
          // and we never got a chance to learn its GitHubId.
          await context.BulkUpdateHooks(deleted: new[] { hook.Id });
          hook = null;
        }
      }

      // New connection is efficient because we use stored procedures from here on out.
      using (var context = new ShipHubContext()) {
        if (hook == null) {
          var existingHooks = (await client.OrganizationWebhooks(org.Login, GitHubCacheDetails.Empty)).Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{apiHostName}/", StringComparison.OrdinalIgnoreCase));

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in existingHooks) {
            var deleteResponse = await client.DeleteOrganizationWebhook(org.Login, existingHook.Id);
            if (!deleteResponse.Succeeded || !deleteResponse.Result) {
              Trace.TraceWarning($"Failed to delete existing hook ({existingHook.Id}) for org '{org.Login}'");
            }
          }

          //// GitHub will immediately send a ping when the webhook is created.
          // To avoid any chance for a race, add the Hook to the DB first, then
          // create on GitHub.
          var newHook = await context.CreateHook(Guid.NewGuid(), string.Join(",", requiredEvents), organizationId: org.Id);

          bool deleteHook = false;
          try {
            var addHookResponse = await client.AddOrganizationWebhook(
              org.Login,
              new gm.Webhook() {
                Name = "web",
                Active = true,
                Events = requiredEvents,
                Config = new gm.WebhookConfiguration() {
                  Url = $"https://{apiHostName}/webhook/org/{org.Id}",
                  ContentType = "json",
                  Secret = newHook.Secret.ToString(),
                },
              });

            if (addHookResponse.Succeeded) {
              newHook.GitHubId = addHookResponse.Result.Id;
              var changeSummary = await context.BulkUpdateHooks(hooks: new[] { newHook });
              await notifyChanges.AddAsync(new ChangeMessage(changeSummary));
            } else {
              Trace.TraceWarning($"Failed to add hook for org '{org.Login}' ({org.Id}): {addHookResponse.Status} {addHookResponse.Error?.ToException()}");
              deleteHook = true;
            }
          } catch (Exception e) {
            e.Report($"Failed to add hook for org '{org.Login}' ({org.Id})");
            deleteHook = true;
            throw;
          } finally {
            if (deleteHook) {
              await context.BulkUpdateHooks(deleted: new[] { newHook.Id });
            }
          }
        } else if (!new HashSet<string>(hook.Events.Split(',')).SetEquals(requiredEvents)) {
          var editResponse = await client.EditOrganizationWebhookEvents(org.Login, (long)hook.GitHubId, requiredEvents);

          if (!editResponse.Succeeded) {
            Trace.TraceWarning($"Failed to edit hook for org '{org.Login}' ({org.Id}): {editResponse.Status} {editResponse.Error?.ToException()}");
          } else {
            await context.BulkUpdateHooks(hooks: new[] {
              new HookTableType(){
                Id = hook.Id,
                GitHubId = editResponse.Result.Id,
                Secret = hook.Secret,
                Events = string.Join(",", editResponse.Result.Events),
              }
            });
          }
        }
      }
    }
  }
}
