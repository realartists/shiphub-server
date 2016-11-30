namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Linq;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common.DataModel;
  using Common.GitHub;
  using Microsoft.Azure.WebJobs;
  using Orleans;
  using Tracing;

  public class WebhookReaperTimer : LoggingHandlerBase {
    private IGrainFactory _grainFactory;

    public WebhookReaperTimer(IGrainFactory grainFactory, IDetailedExceptionLogger logger)
      : base(logger) {
      _grainFactory = grainFactory;
    }

    public virtual IGitHubActor CreateGitHubClient(User user, Guid correlationId) {
      return _grainFactory.GetGrain<IGitHubActor>(user.Id);
    }

    public virtual DateTimeOffset UtcNow {
      get {
        return DateTimeOffset.UtcNow;
      }
    }

    [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "timerInfo")]
    public async Task ReaperTimer(
      [TimerTrigger("0 */10 * * * *")] TimerInfo timerInfo,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, null, null, async () => {
        await Run(executionContext.InvocationId.ToString());
      });
    }

    public async Task Run(string correlationId) {
      while (true) {
        using (var context = new ShipHubContext()) {
          var now = UtcNow;
          var maxPingCount = 3;
          var staleDateTimeOffset = now.AddDays(-1);
          var pingTime = now.AddMinutes(-30);
          var batchSize = 100;

          var staleHooks = context.Hooks
           .Where(x =>
             (x.LastSeen <= staleDateTimeOffset) &&
             (x.LastPing == null || x.LastPing <= pingTime) &&
             (x.GitHubId != null))
           .Take(batchSize)
           .ToList();

          var pingTasks = new List<Task<GitHubResponse<bool>>>();

          foreach (var hook in staleHooks) {
            if (hook.PingCount >= maxPingCount) {
              // We've pinged this hook several times now and never heard back.
              // We'll remove it.  The hook will get re-added later when a user
              // with admin privileges for that app uses Ship again and triggers
              // another sync.
              context.Hooks.Remove(hook);
            } else if (hook.RepositoryId != null) {
              // Find some account with admin privileges for this repo that we can
              // use to ping.
              var accountRepository = await context.AccountRepositories
                .Include(x => x.Account)
                .Include(x => x.Repository)
                .Where(x => x.RepositoryId == hook.RepositoryId && x.Admin && x.Account.Token != null)
                .OrderBy(x => x.AccountId)
                .FirstOrDefaultAsync();

              if (accountRepository != null) {
                var client = CreateGitHubClient(accountRepository.Account, Guid.NewGuid());
                pingTasks.Add(client.PingRepositoryWebhook(accountRepository.Repository.FullName, (long)hook.GitHubId));

                hook.PingCount = hook.PingCount == null ? 1 : hook.PingCount + 1;
                hook.LastPing = now;
              }
            } else if (hook.OrganizationId != null) {
              var accountOrganization = await context.OrganizationAccounts
                .Include(x => x.User)
                .Include(x => x.Organization)
                .Where(x => x.OrganizationId == hook.OrganizationId && x.Admin && x.User.Token != null)
                .OrderBy(x => x.UserId)
                .FirstOrDefaultAsync();

              if (accountOrganization != null) {
                var client = CreateGitHubClient(accountOrganization.User, Guid.NewGuid());
                pingTasks.Add(client.PingOrganizationWebhook(accountOrganization.Organization.Login, (long)hook.GitHubId));

                hook.PingCount = hook.PingCount == null ? 1 : hook.PingCount + 1;
                hook.LastPing = now;
              }
            } else {
              throw new InvalidOperationException("RepositoryId or OrganizationId should be non null");
            }
          }
          await context.SaveChangesAsync();
          await Task.WhenAll(pingTasks);

          if (staleHooks.Count < batchSize) {
            break;
          }
        }
      }
    }
  }
}
