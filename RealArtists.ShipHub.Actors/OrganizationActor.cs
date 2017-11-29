namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
  using System.Collections.Immutable;
  using System.Data.Entity;
  using System.Linq;
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

    public static ImmutableHashSet<string> RequiredEvents { get; } = ImmutableHashSet.Create(
      //"membership"
      //, "organization"
      "repository"
      //, "team"
    );

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
    private GitHubMetadata _hookMetadata;

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

      await Save();
      await base.OnDeactivateAsync();
    }

    private async Task Save() {
      using (var context = _contextFactory.CreateInstance()) {
        // MUST MATCH LOAD
        await context.UpdateMetadata("Accounts", _orgId, _metadata);
        await context.UpdateMetadata("Accounts", "OrgMetadataJson", _orgId, _adminMetadata);
        await context.UpdateMetadata("Accounts", "ProjectMetadataJson", _orgId, _projectMetadata);
      }
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
        _grainFactory.GetGrain<IUserActor>(userId).SyncRepositories().LogFailure();
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

      var updater = new DataUpdater(_contextFactory, _mapper);
      try {
        await UpdateDetails(updater, github);
        await UpdateAdmins(updater, github);
        await UpdateProjects(updater, github);

        // Webhooks
        if (admin != null) {
          updater.UnionWithExternalChanges(await UpdateHookWithAdmin(admin));
        } else {
          // No matter what, we can't add a hook (no admin user)
          // As of right now the org hook is only used to watch for added/removed repos
          // It's not essential, so let stale hooks remain and do not signal that help is needed.
          // If an admin subsequently starts using ship, or current users' permissions change, we'll fix things.
        }
      } catch (GitHubPoolEmptyException) {
        // Nothing to do.
        // No need to also catch GithubRateLimitException, it's handled by GitHubActorPool
      }

      // Send Changes.
      await updater.Changes.Submit(_queueClient);

      // Save
      await Save();
    }

    private async Task UpdateDetails(DataUpdater updater, IGitHubPoolable github) {
      if (_metadata.IsExpired()) {
        var org = await github.Organization(_orgId, _metadata);
        if (org.IsOk) {
          await updater.UpdateAccounts(org.Date, new[] { org.Result });
          // Update login For the rest of sync.
          _login = org.Result.Login;
          // Safest to just start over.
          DeactivateOnIdle();
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
          throw new Exception($"Unexpected response: [{admins?.Request?.Uri}] {admins?.Status}");
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

    public async Task<IChangeSummary> UpdateHookWithAdmin(IGitHubOrganizationAdmin admin) {
      if (admin == null) {
        throw new ArgumentNullException(nameof(admin));
      }

      var changes = new ChangeSummary();
      var notify = true;

      using (var context = _contextFactory.CreateInstance()) {
        HookTableType hookRecord = null;
        DateTimeOffset? lastError = DateTimeOffset.UtcNow;

        // First, do we think there is a hook?
        var hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == _orgId);
        context.Database.Connection.Close();

        // If our last operation on this repo hook resulted in error, delay.
        // This is pretty hacky, and ideally won't happen often.
        if (hook?.LastError != null && hook.LastError.Value > DateTimeOffset.UtcNow.Subtract(HookErrorDelay)) {
          return ChangeSummary.Empty; ; // Wait to try later.
        }

        // Ensure there exists a hook record on our end if only for error tracking.
        if (hook == null) {
          hookRecord = await context.CreateHook(SecureRandom.GenerateGuid(), string.Join(",", RequiredEvents), organizationId: _orgId);
        } else {
          hookRecord = new HookTableType() {
            Id = hook.Id,
            Secret = hook.Secret,
            Events = hook.Events,
            GitHubId = hook.GitHubId,
          };
        }

        try {
          // No matter what, we need to know about exant hooks on GitHub's side.
          // TODO: CACHE!
          var hookList = await admin.OrganizationWebhooks(_login, _hookMetadata);
          _hookMetadata = GitHubMetadata.FromResponse(hookList);

          // Check for cache hit (no need to do anything) or error (log)
          if (!hookList.IsOk) {
            if (!hookList.Succeeded) {
              this.Info($"Unable to list hooks for {_login}. {hookList.Status} {hookList.Error}");
            }
            return ChangeSummary.Empty;
          }

          var webhooks = hookList.Result.Where(x => x.Name.Equals("web")).ToList();
          var deleteHooks = webhooks
            .Where(x => x.Config.Url.StartsWith($"https://{_apiHostName}/webhook/org/", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Id != hookRecord.GitHubId)
            .ToArray();

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in deleteHooks) {
            var deleteResponse = await admin.DeleteOrganizationWebhook(_login, existingHook.Id);
            if (!deleteResponse.Succeeded) {
              this.Info($"Failed to delete existing hook ({existingHook.Id}) for org '{_login}' {deleteResponse.Status} {deleteResponse.Error}");
            } else {
              webhooks.Remove(existingHook);
            }
          }

          var eventCounts = webhooks
            .Where(x => x.Id != hookRecord.GitHubId) // Don't count existing hook, if present
            .SelectMany(x => x.Events)
            .GroupBy(x => x)
            .ToDictionary(x => x.Key, x => x.Count());

          // https://developer.github.com/webhooks/
          // You can create up to 20 webhooks for each event on each installation target (specific organization or specific repository).
          // Don't try to add oversubscribed events
          var computedEvents = RequiredEvents.Except(eventCounts.Where(x => x.Value >= 20).Select(x => x.Key));
          if (computedEvents.SetEquals(RequiredEvents) == false) {
            this.Info($"Skipping oversubscribed events: [{string.Join(",", RequiredEvents.Except(computedEvents))}]");
          }

          var currentHook = webhooks.SingleOrDefault(x => x.Id == hookRecord.GitHubId);
          if (currentHook == null) {
            // Let's make it!
            var response = await admin.AddOrganizationWebhook(
              _login,
              new gh.Webhook() {
                Name = "web",
                Active = true,
                Events = computedEvents,
                Config = new gh.WebhookConfiguration() {
                  Url = $"https://{_apiHostName}/webhook/org/{_orgId}",
                  ContentType = "json",
                  Secret = hookRecord.Secret.ToString(),
                },
              });

            if (response.Succeeded) {
              hookRecord.Events = string.Join(",", response.Result.Events);
              hookRecord.GitHubId = response.Result.Id;
              lastError = null;
            } else {
              this.Error($"Failed to add hook for org '{_login}' ({_orgId}): {response.Status} {response.Error}");
            }
          } else if (computedEvents.SetEquals(currentHook.Events) == false || currentHook.Active == false) {
            // Update the hook
            notify = false; // Don't notify for updates.
            this.Info($"Updating webhook {_login}/{currentHook.Id} from Events: [{string.Join(",", currentHook.Events)}] to [{string.Join(",", computedEvents)}] and Active: [{currentHook.Active} => true]");
            var response = await admin.EditOrganizationWebhookEvents(_login, currentHook.Id, computedEvents);

            if (response.Succeeded) {
              hookRecord.Events = string.Join(",", response.Result.Events);
              hookRecord.GitHubId = response.Result.Id;
              lastError = null;
            } else {
              this.Error($"Failed to edit hook for org '{_login}' ({_orgId}): {response.Status} {response.Error}");
            }
          }
        } catch (Exception e) {
          notify = false;
          e.Report($"Error processing hooks for org '{_login}' ({_orgId})");
        } finally {
          hookRecord.LastError = lastError;
          changes = await context.BulkUpdateHooks(hooks: new[] { hookRecord });
        }
      }

      return notify ? changes : ChangeSummary.Empty;
    }
  }
}
