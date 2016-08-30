﻿namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics;
  using System.Linq;
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Microsoft.Azure;
  using Microsoft.Azure.WebJobs;
  using QueueClient;
  using QueueClient.Messages;
  using gm = Common.GitHub.Models;

  public static class WebhookHandler {
    public static Task AddOrUpdateRepoWebhooks(
      [ServiceBusTrigger(ShipHubQueueNames.AddOrUpdateRepoWebhooks)] AddOrUpdateRepoWebhooksMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      return AddOrUpdateRepoWebhooksWithClient(message, GitHubSettings.CreateUserClient(message.AccessToken), notifyChanges);
    }

    public static async Task AddOrUpdateRepoWebhooksWithClient(
      AddOrUpdateRepoWebhooksMessage message,
      IGitHubClient client,
      IAsyncCollector<ChangeMessage> notifyChanges) {
      using (var context = new ShipHubContext()) {
        var repo = await context.Repositories.SingleAsync(x => x.Id == message.RepositoryId);
        var requiredEvents = new string[] {
          "issues",
          "issue_comment",
          //"member",
          //"public",
          //"pull_request",
          //"pull_request_review_comment",
          //"repository",
          //"team_add",
        };
        var apiHostname = CloudConfigurationManager.GetSetting("ApiHostname");
        if (apiHostname == null) {
          throw new ApplicationException("ApiHostname not specified in configuration.");
        }

        var hook = await context.Hooks.SingleOrDefaultAsync(x => x.RepositoryId == message.RepositoryId);

        if (hook == null) {
          var existingHooks = (await client.RepoWebhooks(repo.FullName)).Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{apiHostname}/"));

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in existingHooks) {
            var deleteResponse = await client.DeleteRepoWebhook(repo.FullName, existingHook.Id);
            if (deleteResponse.IsError || !deleteResponse.Result) {
              Trace.TraceWarning($"Failed to delete existing hook ({existingHook.Id}) for repo '{repo.FullName}'");
            }
          }

          // GitHub will immediately send a ping when the webhook is created.
          // To avoid any chance for a race, add the Hook to the DB first, then
          // create on GitHub.
          hook = context.Hooks.Add(new Hook() {
            Active = false,
            Events = string.Join(",", requiredEvents),
            Secret = Guid.NewGuid(),
            RepositoryId = repo.Id,
          });
          await context.SaveChangesAsync();

          var addRepoHookResponse = await client.AddRepoWebhook(
            repo.FullName,
            new gm.Webhook() {
              Name = "web",
              Active = true,
              Events = requiredEvents,
              Config = new gm.WebhookConfiguration() {
                Url = $"https://{apiHostname}/webhook/repo/{repo.Id}",
                ContentType = "json",
                Secret = hook.Secret.ToString(),
              },
            });

          if (!addRepoHookResponse.IsError) {
            hook.GitHubId = addRepoHookResponse.Result.Id;
            await context.SaveChangesAsync();

            await context.BumpRepositoryVersion(repo.Id);

            var changeSummary = new ChangeSummary();
            changeSummary.Repositories.Add(repo.Id);
            await notifyChanges.AddAsync(new ChangeMessage(changeSummary));
          } else {
            Trace.TraceWarning($"Failed to add hook for repo '{repo.FullName}': {addRepoHookResponse.Error}");
            context.Hooks.Remove(hook);
            await context.SaveChangesAsync();
          }
        } else if (!new HashSet<string>(hook.Events.Split(',')).SetEquals(requiredEvents)) {
          var editResponse = await client.EditRepoWebhookEvents(repo.FullName, hook.GitHubId, requiredEvents);

          if (editResponse.IsError) {
            Trace.TraceWarning($"Failed to edit hook for repo '{repo.FullName}': {editResponse.Error}");
          } else {
            hook.Events = string.Join(",", editResponse.Result.Events);
            await context.SaveChangesAsync();
          }
        }
      }
    }

    public static Task AddOrUpdateOrgWebhooks(
      [ServiceBusTrigger(ShipHubQueueNames.AddOrUpdateOrgWebhooks)] AddOrUpdateOrgWebhooksMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      return AddOrUpdateOrgWebhooksWithClient(message, GitHubSettings.CreateUserClient(message.AccessToken), notifyChanges);
    }

    public static async Task AddOrUpdateOrgWebhooksWithClient(
      AddOrUpdateOrgWebhooksMessage message,
      IGitHubClient client,
      IAsyncCollector<ChangeMessage> notifyChanges) {
      using (var context = new ShipHubContext()) {
        var org = await context.Organizations.SingleAsync(x => x.Id == message.OrganizationId);
        var requiredEvents = new string[] {
          "repository",
        };
        var apiHostname = CloudConfigurationManager.GetSetting("ApiHostname");
        if (apiHostname == null) {
          throw new ApplicationException("ApiHostname not specified in configuration.");
        }

        var hook = await context.Hooks.SingleOrDefaultAsync(x => x.OrganizationId == message.OrganizationId);

        if (hook == null) {
          var existingHooks = (await client.OrgWebhooks(org.Login)).Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{apiHostname}/"));

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in existingHooks) {
            var deleteResponse = await client.DeleteOrgWebhook(org.Login, existingHook.Id);
            if (deleteResponse.IsError || !deleteResponse.Result) {
              Trace.TraceWarning($"Failed to delete existing hook ({existingHook.Id}) for org '{org.Login}'");
            }
          }

          //// GitHub will immediately send a ping when the webhook is created.
          // To avoid any chance for a race, add the Hook to the DB first, then
          // create on GitHub.
          hook = context.Hooks.Add(new Hook() {
            Active = false,
            Events = string.Join(",", requiredEvents),
            Secret = Guid.NewGuid(),
            OrganizationId = org.Id,
          });
          await context.SaveChangesAsync();

          var addResponse = await client.AddOrgWebhook(
            org.Login,
            new gm.Webhook() {
              Name = "web",
              Active = true,
              Events = requiredEvents,
              Config = new gm.WebhookConfiguration() {
                Url = $"https://{apiHostname}/webhook/org/{org.Id}",
                ContentType = "json",
                Secret = hook.Secret.ToString(),
              },
            });

          if (!addResponse.IsError) {
            hook.GitHubId = addResponse.Result.Id;
            await context.SaveChangesAsync();

            await context.BumpOrganizationVersion(org.Id);

            var changeSummary = new ChangeSummary();
            changeSummary.Organizations.Add(org.Id);
            await notifyChanges.AddAsync(new ChangeMessage(changeSummary));
          } else {
            Trace.TraceWarning($"Failed to add hook for org '{org.Login}': {addResponse.Error}");
            context.Hooks.Remove(hook);
            await context.SaveChangesAsync();
          }
        } else if (!new HashSet<string>(hook.Events.Split(',')).SetEquals(requiredEvents)) {
          var editResponse = await client.EditOrgWebhookEvents(org.Login, hook.GitHubId, requiredEvents);

          if (editResponse.IsError) {
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