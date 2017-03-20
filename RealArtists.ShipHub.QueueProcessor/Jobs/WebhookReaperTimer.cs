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
  using RealArtists.ShipHub.Common.DataModel.Types;
  using RealArtists.ShipHub.QueueClient;
  using RealArtists.ShipHub.QueueClient.Messages;
  using Tracing;

  public class WebhookReaperTimer : LoggingHandlerBase {
    private IGrainFactory _grainFactory;

    public WebhookReaperTimer(IGrainFactory grainFactory, IDetailedExceptionLogger logger)
      : base(logger) {
      _grainFactory = grainFactory;
    }

    public virtual IGitHubActor CreateGitHubClient(long userId) {
      return _grainFactory.GetGrain<IGitHubActor>(userId);
    }

    public virtual DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "timerInfo")]
    public async Task ReaperTimer(
      [TimerTrigger("0 */10 * * * *")] TimerInfo timerInfo,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, null, null, async () => {
        await Run(notifyChanges);
      });
    }

    public async Task Run(IAsyncCollector<ChangeMessage> notifyChanges) {
      var maxPingCount = 3;
      var batchSize = 100;
      var numStaleHooks = 0;

      do {
        var now = UtcNow;
        var staleDateTimeOffset = now.AddDays(-1);
        var pingTime = now.AddMinutes(-30);
        var pingTasks = new List<Task<GitHubResponse<bool>>>();

        ChangeSummary changes;
        using (var context = new ShipHubContext()) {
          var staleHooks = context.Hooks
           .Where(x =>
             (x.LastSeen <= staleDateTimeOffset) &&
             (x.LastPing == null || x.LastPing <= pingTime) &&
             (x.GitHubId != null))
           .Take(batchSize)
           .ToList();
          numStaleHooks = staleHooks.Count;

          var deleted = new HashSet<long>();
          var pinged = new HashSet<long>();
          foreach (var hook in staleHooks) {
            if (hook.PingCount >= maxPingCount) {
              // We've pinged this hook several times now and never heard back.
              // We'll remove it.  The hook will get re-added later when a user
              // with admin privileges for that app uses Ship again and triggers
              // another sync.
              deleted.Add(hook.Id);
            } else if (hook.RepositoryId != null) {
              // Find some account with admin privileges for this repo that we can
              // use to ping.
              var createHookInfo = await context.AccountRepositories
                .AsNoTracking()
                .Where(x => x.Admin)
                .Where(x => x.RepositoryId == hook.RepositoryId)
                .Where(x => x.Account.Tokens.Any())
                .Where(x => x.Account.RateLimit > GitHubRateLimit.RateLimitFloor || x.Account.RateLimitReset < DateTime.UtcNow)
                .Select(x => new { UserId = x.AccountId, RepoFullName = x.Repository.FullName })
                .FirstOrDefaultAsync();

              if (createHookInfo != null) {
                var client = CreateGitHubClient(createHookInfo.UserId);
                pingTasks.Add(client.PingRepositoryWebhook(createHookInfo.RepoFullName, (long)hook.GitHubId));
                pinged.Add(hook.Id);
              }
            } else if (hook.OrganizationId != null) {
              var createHookInfo = await context.OrganizationAccounts
                .AsNoTracking()
                .Where(x => x.Admin)
                .Where(x => x.OrganizationId == hook.OrganizationId)
                .Where(x => x.User.Tokens.Any())
                .Where(x => x.User.RateLimit > GitHubRateLimit.RateLimitFloor || x.User.RateLimitReset < DateTime.UtcNow)
                .Select(x => new { UserId = x.UserId, OrgLogin = x.Organization.Login })
                .FirstOrDefaultAsync();

              if (createHookInfo != null) {
                var client = CreateGitHubClient(createHookInfo.UserId);
                pingTasks.Add(client.PingOrganizationWebhook(createHookInfo.OrgLogin, (long)hook.GitHubId));
                pinged.Add(hook.Id);
              }
            } else {
              throw new InvalidOperationException("RepositoryId or OrganizationId should be non null");
            }
          }

          changes = await context.BulkUpdateHooks(deleted: deleted, pinged: pinged);
        }

        if (!changes.IsEmpty) {
          await notifyChanges.AddAsync(new ChangeMessage(changes));
        }

        await Task.WhenAll(pingTasks);
      } while (numStaleHooks == batchSize);
    }
  }
}
