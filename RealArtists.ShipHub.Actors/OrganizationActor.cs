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

    // Data
    private HashSet<long> _admins = new HashSet<long>();

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

        _admins = org.OrganizationAccounts
          .Where(x => x.Admin)
          .Select(x => x.UserId)
          .ToHashSet();

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

    // ////////////////////////////////////////////////////////////
    // Utility Functions
    // ////////////////////////////////////////////////////////////

    private async Task<IEnumerable<Tuple<long, bool>>> GetUsersWithAccess() {
      using (var context = _contextFactory.CreateInstance()) {
        // TODO: Keep this cached and current instead of looking it up every time.
        var users = await context.OrganizationAccounts
          .AsNoTracking()
          .Where(x => x.OrganizationId == _orgId)
          .Where(x => x.User.Token != null)
          .Where(x => x.User.RateLimit > GitHubRateLimit.RateLimitFloor || x.User.RateLimitReset < DateTime.UtcNow)
          .Select(x => new { UserId = x.UserId, Admin = x.Admin })
          .ToArrayAsync();

        return users
          .Select(x => Tuple.Create(x.UserId, x.Admin))
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

      var github = new GitHubActorPool(_grainFactory, users.Select(x => x.Item1));

      IGitHubOrganizationAdmin admin = null;
      var firstAdmin = users.FirstOrDefault(x => x.Item2);
      if (firstAdmin != null) {
        admin = _grainFactory.GetGrain<IGitHubActor>(firstAdmin.Item1);
      }

      var changes = new ChangeSummary();
      try {
        await SyncTask(github, admin, changes);
      } catch (GitHubPoolEmptyException) {
        // Nothing to do.
        // No need to also catch GithubRateLimitException, it's handled by GitHubActorPool
      }

      // Send Changes.
      if (!changes.IsEmpty) {
        await _queueClient.NotifyChanges(changes);
      }

      // Save
      await Save();
    }

    private async Task SyncTask(IGitHubPoolable github, IGitHubOrganizationAdmin admin, ChangeSummary changes) {
      var tasks = new List<Task>();

      using (var context = _contextFactory.CreateInstance()) {
        // Org itself
        if (_metadata.IsExpired()) {
          var org = await github.Organization(_login, _metadata);

          if (org.IsOk) {
            this.Info("Updating Organization");
            changes.UnionWith(
              await context.UpdateAccount(org.Date, _mapper.Map<AccountTableType>(org.Result))
            );
          }

          // Don't update until saved.
          _metadata = GitHubMetadata.FromResponse(org);
        }

        if (_adminMetadata.IsExpired()) {
          var admins = await github.OrganizationMembers(_login, role: "admin", cacheOptions: _adminMetadata);
          if (admins.IsOk) {
            _admins = admins.Result.Select(x => x.Id).ToHashSet();
            changes.UnionWith(await context.BulkUpdateAccounts(admins.Date, _mapper.Map<IEnumerable<AccountTableType>>(admins.Result)));
            changes.UnionWith(await context.SetOrganizationAdmins(_orgId, _admins));

            this.Info($"Changed. Admins: [{string.Join(",", _admins.OrderBy(x => x))}]");
          } else if (!admins.Succeeded) {
            throw new Exception($"Unexpected response: OrganizationAdmins {admins.Status}");
          }

          _adminMetadata = GitHubMetadata.FromResponse(admins);
        }

        // Projects
        changes.UnionWith(await UpdateProjects(context, github));

        // Webhooks
        if (admin != null) {
          changes.UnionWith(await AddOrUpdateOrganizationWebhooks(context, admin));
        }
      }

      // Await all outstanding operations.
      await Task.WhenAll(tasks);
    }

    private async Task<IChangeSummary> UpdateProjects(ShipHubContext context, IGitHubPoolable github) {
      var changes = new ChangeSummary();

      if (_projectMetadata.IsExpired()) {
        var projects = await github.OrganizationProjects(_login, _projectMetadata);
        if (projects.IsOk) {
          var creators = projects.Result.Select(p => new AccountTableType() {
            Type = Account.UserType,
            Login = p.Creator.Login,
            Id = p.Creator.Id
          }).Distinct(t => t.Id);
          if (creators.Any()) {
            changes.UnionWith(await context.BulkUpdateAccounts(projects.Date, creators));
          }
          changes.UnionWith(await context.BulkUpdateOrganizationProjects(_orgId, _mapper.Map<IEnumerable<ProjectTableType>>(projects.Result)));
        }

        _projectMetadata = GitHubMetadata.FromResponse(projects);
      }

      return changes;
    }

    public async Task<IChangeSummary> AddOrUpdateOrganizationWebhooks(ShipHubContext context, IGitHubOrganizationAdmin admin) {
      var changes = ChangeSummary.Empty;

      var hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == _orgId);
      context.Database.Connection.Close();

      if (hook != null && hook.GitHubId == null) {
        // We attempted to add a webhook for this earlier, but something failed
        // and we never got a chance to learn its GitHubId.
        await context.BulkUpdateHooks(deleted: new[] { hook.Id });
        hook = null;
      }

      if (hook == null) {
        var hookList = await admin.OrganizationWebhooks(_login);
        if (!hookList.IsOk) {
          // webhooks are best effort
          // this keeps us from spewing errors and retrying a ton when an org is unpaid
          Log.Info($"Unable to list hooks for {_login}");
          return changes;
        }

        var existingHooks = hookList.Result
          .Where(x => x.Name.Equals("web"))
          .Where(x => x.Config.Url.StartsWith($"https://{_apiHostName}/", StringComparison.OrdinalIgnoreCase));

        // Delete any existing hooks that already point back to us - don't
        // want to risk adding multiple Ship hooks.
        foreach (var existingHook in existingHooks) {
          var deleteResponse = await admin.DeleteOrganizationWebhook(_login, existingHook.Id);
          if (!deleteResponse.Succeeded || !deleteResponse.Result) {
            Log.Info($"Failed to delete existing hook ({existingHook.Id}) for org '{_login}'");
          }
        }

        //// GitHub will immediately send a ping when the webhook is created.
        // To avoid any chance for a race, add the Hook to the DB first, then
        // create on GitHub.
        var newHook = await context.CreateHook(Guid.NewGuid(), string.Join(",", RequiredEvents), organizationId: _orgId);

        bool deleteHook = false;
        try {
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
            changes = await context.BulkUpdateHooks(hooks: new[] { newHook });
          } else {
            Log.Error($"Failed to add hook for org '{_login}' ({_orgId}): {addHookResponse.Status} {addHookResponse.Error?.ToException()}");
            deleteHook = true;
          }
        } catch (Exception e) {
          e.Report($"Failed to add hook for org '{_login}' ({_orgId})");
          deleteHook = true;
          throw;
        } finally {
          if (deleteHook) {
            await context.BulkUpdateHooks(deleted: new[] { newHook.Id });
          }
        }
      } else if (!hook.Events.Split(',').ToHashSet().SetEquals(RequiredEvents)) {
        var editResponse = await admin.EditOrganizationWebhookEvents(_login, (long)hook.GitHubId, RequiredEvents);

        if (!editResponse.Succeeded) {
          Log.Info($"Failed to edit hook for org '{_login}' ({_orgId}): {editResponse.Status} {editResponse.Error?.ToException()}");
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

      return changes;
    }
  }
}
