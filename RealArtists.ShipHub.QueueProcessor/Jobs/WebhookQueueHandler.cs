﻿namespace RealArtists.ShipHub.QueueProcessor.Jobs {
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
      "milestone",
      "push"
    };

    private IShipHubConfiguration _config;
    private IGrainFactory _grainFactory;

    public WebhookQueueHandler(IShipHubConfiguration config, IGrainFactory grainFactory, IDetailedExceptionLogger logger)
      : base(logger) {
      _config = config;
      _grainFactory = grainFactory;
    }

    [Singleton("{TargetId}")]
    public async Task AddOrUpdateRepoWebhooks(
      [ServiceBusTrigger(ShipHubQueueNames.AddOrUpdateRepoWebhooks)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        IGitHubActor gh;
        using (var context = new ShipHubContext()) {
          var user = await context.Users.Where(x => x.Id == message.ForUserId).SingleOrDefaultAsync();
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }
          gh = _grainFactory.GetGrain<IGitHubActor>(user.Id);
        }
        await AddOrUpdateRepoWebhooksWithClient(message, gh, notifyChanges);
      });
    }

    public async Task AddOrUpdateRepoWebhooksWithClient(
      TargetMessage message,
      IGitHubActor client,
      IAsyncCollector<ChangeMessage> notifyChanges) {
      using (var context = new ShipHubContext()) {
        var repo = await context.Repositories.SingleAsync(x => x.Id == message.TargetId);
        var apiHostName = _config.ApiHostName;

        var hook = await context.Hooks.SingleOrDefaultAsync(x => x.RepositoryId == message.TargetId);

        if (hook != null && hook.GitHubId == null) {
          // We attempted to add a webhook for this earlier, but something failed
          // and we never got a chance to learn its GitHubId.
          context.Hooks.Remove(hook);
          await context.SaveChangesAsync();
          hook = null;
        }

        if (hook == null) {
          var existingHooks = (await client.RepositoryWebhooks(repo.FullName, GitHubCacheDetails.Empty)).Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{apiHostName}/"));

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in existingHooks) {
            var deleteResponse = await client.DeleteRepositoryWebhook(repo.FullName, existingHook.Id);
            if (!deleteResponse.Succeeded || !deleteResponse.Result) {
              Trace.TraceWarning($"Failed to delete existing hook ({existingHook.Id}) for repo '{repo.FullName}'");
            }
          }

          // GitHub will immediately send a ping when the webhook is created.
          // To avoid any chance for a race, add the Hook to the DB first, then
          // create on GitHub.
          hook = context.Hooks.Add(new Hook() {
            Events = string.Join(",", RequiredEvents),
            Secret = Guid.NewGuid(),
            RepositoryId = repo.Id,
          });
          await context.SaveChangesAsync();

          var addRepoHookTask = client.AddRepositoryWebhook(
            repo.FullName,
            new gm.Webhook() {
              Name = "web",
              Active = true,
              Events = RequiredEvents,
              Config = new gm.WebhookConfiguration() {
                Url = $"https://{apiHostName}/webhook/repo/{repo.Id}",
                ContentType = "json",
                Secret = hook.Secret.ToString(),
              },
            });
          await Task.WhenAny(addRepoHookTask);

          if (!addRepoHookTask.IsFaulted && addRepoHookTask.Result.Succeeded) {
            hook.GitHubId = addRepoHookTask.Result.Result.Id;
            await context.SaveChangesAsync();

            await context.BumpRepositoryVersion(repo.Id);

            var changeSummary = new ChangeSummary();
            changeSummary.Repositories.Add(repo.Id);
            await notifyChanges.AddAsync(new ChangeMessage(changeSummary));
          } else {
            Trace.TraceWarning($"Failed to add hook for repo '{repo.FullName}': {addRepoHookTask.Exception}");
            context.Hooks.Remove(hook);
            await context.SaveChangesAsync();
          }
        } else if (!new HashSet<string>(hook.Events.Split(',')).SetEquals(RequiredEvents)) {
          var editResponse = await client.EditRepositoryWebhookEvents(repo.FullName, (long)hook.GitHubId, RequiredEvents);

          if (!editResponse.Succeeded) {
            Trace.TraceWarning($"Failed to edit hook for repo '{repo.FullName}': {editResponse.Error}");
          } else {
            hook.Events = string.Join(",", editResponse.Result.Events);
            await context.SaveChangesAsync();
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
        IGitHubActor gh;
        using (var context = new ShipHubContext()) {
          var user = await context.Users.Where(x => x.Id == message.ForUserId).SingleOrDefaultAsync();
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }
          gh = _grainFactory.GetGrain<IGitHubActor>(user.Id);
        }

        await AddOrUpdateOrgWebhooksWithClient(message, gh, notifyChanges);
      });
    }

    public async Task AddOrUpdateOrgWebhooksWithClient(
      TargetMessage message,
      IGitHubActor client,
      IAsyncCollector<ChangeMessage> notifyChanges) {
      using (var context = new ShipHubContext()) {
        var org = await context.Organizations.SingleAsync(x => x.Id == message.TargetId);
        var requiredEvents = new string[] {
          "repository",
        };
        var apiHostName = _config.ApiHostName;

        var hook = await context.Hooks.SingleOrDefaultAsync(x => x.OrganizationId == message.TargetId);

        if (hook != null && hook.GitHubId == null) {
          // We attempted to add a webhook for this earlier, but something failed
          // and we never got a chance to learn its GitHubId.
          context.Hooks.Remove(hook);
          await context.SaveChangesAsync();
          hook = null;
        }

        if (hook == null) {
          var existingHooks = (await client.OrganizationWebhooks(org.Login, GitHubCacheDetails.Empty)).Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{apiHostName}/"));

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
          hook = context.Hooks.Add(new Hook() {
            Events = string.Join(",", requiredEvents),
            Secret = Guid.NewGuid(),
            OrganizationId = org.Id,
          });
          await context.SaveChangesAsync();

          var addTask = client.AddOrganizationWebhook(
            org.Login,
            new gm.Webhook() {
              Name = "web",
              Active = true,
              Events = requiredEvents,
              Config = new gm.WebhookConfiguration() {
                Url = $"https://{apiHostName}/webhook/org/{org.Id}",
                ContentType = "json",
                Secret = hook.Secret.ToString(),
              },
            });
          await Task.WhenAny(addTask);

          if (!addTask.IsFaulted && addTask.Result.Succeeded) {
            hook.GitHubId = addTask.Result.Result.Id;
            await context.SaveChangesAsync();

            await context.BumpOrganizationVersion(org.Id);

            var changeSummary = new ChangeSummary();
            changeSummary.Organizations.Add(org.Id);
            await notifyChanges.AddAsync(new ChangeMessage(changeSummary));
          } else {
            Trace.TraceWarning($"Failed to add hook for org '{org.Login}': {addTask.Exception}");
            context.Hooks.Remove(hook);
            await context.SaveChangesAsync();
          }
        } else if (!new HashSet<string>(hook.Events.Split(',')).SetEquals(requiredEvents)) {
          var editResponse = await client.EditOrganizationWebhookEvents(org.Login, (long)hook.GitHubId, requiredEvents);

          if (!editResponse.Succeeded) {
            Trace.TraceWarning($"Failed to edit hook for org '{org.Login}': {editResponse.Error}");
          } else {
            hook.Events = string.Join(",", editResponse.Result.Events);
            await context.SaveChangesAsync();
          }
        }
      }
    }
  }
}
