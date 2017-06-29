namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
  using System.Collections.Immutable;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using ActorInterfaces;
  using ActorInterfaces.GitHub;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using GitHub;
  using Orleans;
  using QueueClient;
  using gh = Common.GitHub.Models;

  public class OrganizationActor : Grain, IOrganizationActor {
    public static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SyncIdle = TimeSpan.FromSeconds(SyncDelay.TotalSeconds * 3);
    public static readonly TimeSpan HookErrorDelay = TimeSpan.FromHours(1);

    public static ImmutableHashSet<string> RequiredEvents { get; } = ImmutableHashSet.Create("repository");

    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    private long _orgId;
    private string _login;
    private string _apiHostName;

    // Metadata
    private GitHubMetadata _metadata;
    private GitHubMetadata _adminMetadata;
    private GitHubMetadata _projectMetadata;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;

    public OrganizationActor(
      IMapper mapper,
      IGrainFactory grainFactory,
      IFactory<ShipHubContext> contextFactory,
      IShipHubQueueClient queueClient,
      IShipHubConfiguration configuration) {
      _mapper = mapper;
      _grainFactory = grainFactory;
      _contextFactory = contextFactory;
      _queueClient = queueClient;
      _apiHostName = configuration.ApiHostName;
    }

    public override async Task OnActivateAsync() {
      using (var context = _contextFactory.CreateInstance()) {
        var orgId = this.GetPrimaryKeyLong();

        // Ensure this organization actually exists
        var org = await context.Organizations
          .Include(x => x.OrganizationAccounts)
          .SingleOrDefaultAsync(x => x.Id == orgId);

        if (org == null) {
          throw new InvalidOperationException($"Organization {orgId} does not exist and cannot be activated.");
        }

        Initialize(org.Id, org.Login);

        // MUST MATCH SAVE
        _metadata = org.Metadata;
        _adminMetadata = org.OrganizationMetadata;
        _projectMetadata = org.ProjectMetadata;
      }

      await base.OnActivateAsync();
    }

    public void Initialize(long orgId, string login) {
      _orgId = orgId;
      _login = login;
    }

    public override async Task OnDeactivateAsync() {
      _syncTimer?.Dispose();
      _syncTimer = null;

      using (var context = _contextFactory.CreateInstance()) {
        await Save(context);
      }
      await base.OnDeactivateAsync();
    }

    private async Task Save(ShipHubContext context) {
      // MUST MATCH LOAD
      await context.UpdateMetadata("Accounts", _orgId, _metadata);
      await context.UpdateMetadata("Accounts", "OrgMetadataJson", _orgId, _adminMetadata);
      await context.UpdateMetadata("Accounts", "ProjectMetadataJson", _orgId, _projectMetadata);
    }

    public async Task ForceSyncAllMemberRepositories() {
      IEnumerable<long> memberIds;
      using (var context = _contextFactory.CreateInstance()) {
        memberIds = await context.OrganizationAccounts
          .Where(x => x.OrganizationId == _orgId)
          .Where(x => x.User.Tokens.Any())
          .Select(x => x.User.Id)
          .ToArrayAsync();
      }

      // Best Effort
      foreach (var userId in memberIds) {
        _grainFactory.GetGrain<IUserActor>(userId).ForceSyncRepositories().LogFailure();
      }
    }

    // ////////////////////////////////////////////////////////////
    // Utility Functions
    // ////////////////////////////////////////////////////////////

    private async Task<IEnumerable<(long UserId, bool IsAdmin)>> GetUsersWithAccess() {
      using (var context = _contextFactory.CreateInstance()) {
        // TODO: Keep this cached and current instead of looking it up every time.
        var users = await context.OrganizationAccounts
          .AsNoTracking()
          .Where(x => x.OrganizationId == _orgId)
          .Where(x => x.User.Tokens.Any())
          .Where(x => x.User.RateLimit > GitHubRateLimit.RateLimitFloor || x.User.RateLimitReset < DateTime.UtcNow)
          .Select(x => new { UserId = x.UserId, Admin = x.Admin })
          .ToArrayAsync();

        return users
          .Select(x => (UserId: x.UserId, IsAdmin: x.Admin))
          .ToArray();
      }
    }

    // ////////////////////////////////////////////////////////////
    // Sync
    // ////////////////////////////////////////////////////////////

    public Task Sync() {
      // For now, calls to sync just indicate interest in syncing.
      // Rather than sync here, we just ensure that a timer is registered.
      _lastSyncInterest = DateTimeOffset.UtcNow;

      if (_syncTimer == null) {
        _syncTimer = RegisterTimer(SyncTimerCallback, null, TimeSpan.Zero, SyncDelay);
      }

      return Task.CompletedTask;
    }

    private async Task SyncTimerCallback(object state) {
      if (DateTimeOffset.UtcNow.Subtract(_lastSyncInterest) > SyncIdle) {
        DeactivateOnIdle();
        return;
      }

      var users = await GetUsersWithAccess();

      if (!users.Any()) {
        DeactivateOnIdle();
        return;
      }

      var github = new GitHubActorPool(_grainFactory, users.Select(x => x.UserId));

      IGitHubOrganizationAdmin admin = null;
      if (users.Any(x => x.IsAdmin)) {
        admin = _grainFactory.GetGrain<IGitHubActor>(users.First(x => x.IsAdmin).UserId);
      }

      using (var context = _contextFactory.CreateInstance()) {
        var updater = new DataUpdater(context, _mapper);
        try {
          await UpdateDetails(updater, github);
          await UpdateAdmins(updater, github);
          await UpdateProjects(updater, github);

          // Webhooks
          if (admin != null) {
            updater.UnionWithExternalChanges(await AddOrUpdateOrganizationWebhooks(context, admin));
          }
        } catch (GitHubPoolEmptyException) {
          // Nothing to do.
          // No need to also catch GithubRateLimitException, it's handled by GitHubActorPool
        }

        // Send Changes.
        await updater.Changes.Submit(_queueClient);

        // Save
        await Save(context);
      }
    }

    private async Task UpdateDetails(DataUpdater updater, IGitHubPoolable github) {
      if (_metadata.IsExpired()) {
        var org = await github.Organization(_login, _metadata);
        if (org.IsOk) {
          await updater.UpdateAccounts(org.Date, new[] { org.Result });
        }
        _metadata = GitHubMetadata.FromResponse(org);
      }
    }

    private async Task UpdateAdmins(DataUpdater updater, IGitHubPoolable github) {
      if (_adminMetadata.IsExpired()) {
        var admins = await github.OrganizationMembers(_login, role: "admin", cacheOptions: _adminMetadata);
        if (admins.IsOk) {
          await updater.SetOrganizationAdmins(_orgId, admins.Date, admins.Result);
        } else if (!admins.Succeeded) {
          throw new Exception($"Unexpected response: [{admins.Request.Uri}] {admins.Status}");
        }
        _adminMetadata = GitHubMetadata.FromResponse(admins);
      }
    }

    private async Task UpdateProjects(DataUpdater updater, IGitHubPoolable github) {
      if (_projectMetadata.IsExpired()) {
        var projects = await github.OrganizationProjects(_login, _projectMetadata);
        if (projects.IsOk) {
          await updater.UpdateOrganizationProjects(_orgId, projects.Date, projects.Result);
        }
        _projectMetadata = GitHubMetadata.FromResponse(projects);
      }
    }

    public async Task<IChangeSummary> AddOrUpdateOrganizationWebhooks(ShipHubContext context, IGitHubOrganizationAdmin admin) {
      var changes = ChangeSummary.Empty;

      var hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == _orgId);
      context.Database.Connection.Close();

      // If our last operation on this repo hook resulted in error, delay.
      if (hook?.LastError != null && hook?.LastError.Value > DateTimeOffset.UtcNow.Subtract(HookErrorDelay)) {
        return changes; // Wait to try later.
      }

      // There are now a few cases to handle
      // If there is no record of a hook, try to make one.
      // If there is an incomplete record, try to make it.
      // If there is an errored record, sleep or retry
      if (hook?.GitHubId == null) {
        // GitHub will immediately send a ping when the webhook is created.
        // To avoid any chance for a race, add the Hook to the DB first, then
        // create on GitHub.
        HookTableType newHook = null;
        if (hook == null) {
          newHook = await context.CreateHook(Guid.NewGuid(), string.Join(",", RequiredEvents), organizationId: _orgId);
        } else {
          newHook = new HookTableType() {
            Id = hook.Id,
            Secret = hook.Secret,
            Events = string.Join(",", RequiredEvents),
          };
        }

        // Assume failure until we succeed
        newHook.LastError = DateTimeOffset.UtcNow;

        try {
          var hookList = await admin.OrganizationWebhooks(_login);
          if (!hookList.IsOk) {
            this.Info($"Unable to list hooks for {_login}. {hookList.Status} {hookList.Error}");
            return changes;
          }

          var existingHooks = hookList.Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{_apiHostName}/", StringComparison.OrdinalIgnoreCase));

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in existingHooks) {
            var deleteResponse = await admin.DeleteOrganizationWebhook(_login, existingHook.Id);
            if (!deleteResponse.Succeeded) {
              this.Info($"Failed to delete existing hook ({existingHook.Id}) for org '{_login}' {deleteResponse.Status} {deleteResponse.Error}");
            }
          }

          var addHookResponse = await admin.AddOrganizationWebhook(
            _login,
            new gh.Webhook() {
              Name = "web",
              Active = true,
              Events = RequiredEvents,
              Config = new gh.WebhookConfiguration() {
                Url = $"https://{_apiHostName}/webhook/org/{_orgId}",
                ContentType = "json",
                Secret = newHook.Secret.ToString(),
              },
            });

          if (addHookResponse.Succeeded) {
            newHook.GitHubId = addHookResponse.Result.Id;
            newHook.LastError = null;
            changes = await context.BulkUpdateHooks(hooks: new[] { newHook });
          } else {
            this.Error($"Failed to add hook for org '{_login}' ({_orgId}): {addHookResponse.Status} {addHookResponse.Error}");
          }
        } catch (Exception e) {
          e.Report($"Failed to add hook for org '{_login}' ({_orgId})");
          // Save LastError
          await context.BulkUpdateHooks(hooks: new[] { newHook });
        }
      } else if (!RequiredEvents.SetEquals(hook.Events.Split(','))) {
        var editHook = new HookTableType() {
          Id = hook.Id,
          GitHubId = hook.GitHubId,
          Secret = hook.Secret,
          Events = hook.Events,
          LastError = DateTimeOffset.UtcNow, // Default to faulted.
        };

        try {
          this.Info($"Updating webhook {_login}/{hook.GitHubId} from [{hook.Events}] to [{string.Join(",", RequiredEvents)}]");
          var editResponse = await admin.EditOrganizationWebhookEvents(_login, (long)hook.GitHubId, RequiredEvents);

          if (editResponse.Succeeded) {
            editHook.LastError = null;
            editHook.GitHubId = editResponse.Result.Id;
            editHook.Events = string.Join(",", editResponse.Result.Events);
            await context.BulkUpdateHooks(hooks: new[] { editHook });
          } else if (editResponse.Status == HttpStatusCode.NotFound) {
            // Our record is out of date.
            this.Info($"Failed to edit hook for org '{_login}'. Deleting our hook record. {editResponse.Status} {editResponse.Error}");
            changes = await context.BulkUpdateHooks(deleted: new[] { editHook.Id });
          } else {
            throw new Exception($"Failed to edit hook for org '{_login}': {editResponse.Status} {editResponse.Error}");
          }
        } catch (Exception e) {
          e.Report();
          // Save LastError
          await context.BulkUpdateHooks(hooks: new[] { editHook });
        }
      }

      return changes;
    }
  }
}
