﻿namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Linq;
  using System.Data.Entity;
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Microsoft.Azure.WebJobs;
  using System.Collections.Generic;

  public class WebhookReaper {

    public static Task Timer([TimerTrigger("* * */2 * * *")] TimerInfo timerInfo) {
      return new WebhookReaper().Run();
    }

    public virtual IGitHubClient CreateGitHubClient(string accessToken) {
      return GitHubSettings.CreateUserClient(accessToken);
    }

    public async Task Run() {
      using (var context = new ShipHubContext()) {
        var maxPingCount = 3;
        var staleDateTimeOffset = DateTimeOffset.UtcNow.AddDays(-1);
        var staleHooks = context.Hooks
         .Where(x => x.LastSeen <= staleDateTimeOffset);

        var pingTasks = new List<Task<GitHubResponse<bool>>>();

        foreach (var hook in staleHooks) {
          if (hook.PingCount >= maxPingCount) {
            // We've pinged this hook several times now and never heard back.
            // We'll remove it.  The hook will get re-added later when a user
            // with admin privileges for that app uses Ship again and triggers
            // another sync.
            context.Hooks.Remove(hook);
          } else if (hook.RepositoryId != null) {
            hook.PingCount = hook.PingCount == null ? 1 : hook.PingCount + 1;

            // Find some account with admin privileges for this repo that we can
            // use to ping.
            var accountRepository = await context.AccountRepositories
              .Include(x => x.Account)
              .Include(x => x.Repository)
              .Where(x => x.RepositoryId == hook.RepositoryId && x.Admin && x.Account.Token != null)
              .OrderBy(x => x.AccountId)
              .FirstOrDefaultAsync();

            if (accountRepository != null) {
              var client = CreateGitHubClient(accountRepository.Account.Token);
              pingTasks.Add(client.PingRepoWebhook(accountRepository.Repository.FullName, hook.Id));
            }
          } else if (hook.OrganizationId != null) {
            hook.PingCount = hook.PingCount == null ? 1 : hook.PingCount + 1;

            var accountOrganization = await context.AccountOrganizations
              .Include(x => x.User)
              .Include(x => x.Organization)
              .Where(x => x.OrganizationId == hook.OrganizationId && x.Admin && x.User.Token != null)
              .OrderBy(x => x.UserId)
              .FirstOrDefaultAsync();

            if (accountOrganization != null) {
              var client = CreateGitHubClient(accountOrganization.User.Token);
              pingTasks.Add(client.PingOrgWebhook(accountOrganization.Organization.Login, hook.Id));
            }
          } else {
            throw new InvalidOperationException("RepositoryId or OrganizationId should be non null");
          }
        }
        await context.SaveChangesAsync();
        await Task.WhenAll(pingTasks);
      }
    }
  }
}
