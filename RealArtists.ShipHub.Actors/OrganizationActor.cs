namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
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

    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    private long _orgId;
    private string _login;

    // Metadata
    private GitHubMetadata _metadata;
    private GitHubMetadata _adminMetadata;
    private GitHubMetadata _memberMetadata;
    private GitHubMetadata _projectMetadata;

    // Data
    private HashSet<long> _members = new HashSet<long>();
    private HashSet<long> _admins = new HashSet<long>();

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;
    private Random _random = new Random();

    public OrganizationActor(IMapper mapper, IGrainFactory grainFactory, IFactory<ShipHubContext> contextFactory, IShipHubQueueClient queueClient) {
      _mapper = mapper;
      _grainFactory = grainFactory;
      _contextFactory = contextFactory;
      _queueClient = queueClient;
    }

    public override async Task OnActivateAsync() {
      using (var context = _contextFactory.CreateInstance()) {
        _orgId = this.GetPrimaryKeyLong();

        // Ensure this organization actually exists
        var org = await context.Organizations
          .Include(x => x.OrganizationAccounts)
          .SingleOrDefaultAsync(x => x.Id == _orgId);

        if (org == null) {
          this.Info("Cannot activate grain. Organization does not exist.");
          throw new InvalidOperationException($"Organization {_orgId} does not exist and cannot be activated.");
        }

        _login = org.Login;

        _members = org.OrganizationAccounts
          .Where(x => !x.Admin)
          .Select(x => x.UserId)
          .ToHashSet();

        _admins = org.OrganizationAccounts
          .Where(x => x.Admin)
          .Select(x => x.UserId)
          .ToHashSet();

        // This is kind of a gross hack to save DB fields. I have mixed feelings about it.
        // MUST MATCH SAVE
        _metadata = org.Metadata;
        _memberMetadata = org.MemberMetadata; // RepoMetadataJson behind the scenes
        _adminMetadata = org.OrganizationMetadata;
        _projectMetadata = org.ProjectMetadata;
      }

      await base.OnActivateAsync();
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
        // RepoMetadataJson here IS NOT A BUG, just a nasty hack
        await context.UpdateMetadata("Accounts", "RepoMetadataJson", _orgId, _memberMetadata);
        await context.UpdateMetadata("Accounts", "OrgMetadataJson", _orgId, _adminMetadata);
        await context.UpdateMetadata("Accounts", "ProjectMetadataJson", _orgId, _projectMetadata);
      }
    }

    // ////////////////////////////////////////////////////////////
    // Utility Functions
    // ////////////////////////////////////////////////////////////

    private async Task<IGitHubPoolable> GetOrganizationActorPool() {
      using (var context = _contextFactory.CreateInstance()) {
        // TODO: Keep this cached and current instead of looking it up every time.
        var syncUserIds = await context.OrganizationAccounts
          .AsNoTracking()
          .Where(x => x.OrganizationId == _orgId)
          .Where(x => x.User.Token != null)
          .Where(x => x.User.RateLimit > GitHubRateLimit.RateLimitFloor || x.User.RateLimitReset < DateTime.UtcNow)
          .Select(x => x.UserId)
          .ToArrayAsync();

        if (syncUserIds.Length == 0) {
          return null;
        }

        return new GitHubActorPool(_grainFactory, syncUserIds);
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
        _syncTimer = RegisterTimer(SyncCallback, null, TimeSpan.Zero, SyncDelay);
      }

      return Task.CompletedTask;
    }

    private async Task SyncCallback(object state) {
      if (DateTimeOffset.UtcNow.Subtract(_lastSyncInterest) > SyncIdle) {
        DeactivateOnIdle();
        return;
      }

      var tasks = new List<Task>();
      var changes = new ChangeSummary();
      var github = await GetOrganizationActorPool();

      if (github == null) {
        DeactivateOnIdle();
        return;
      }

      using (var context = _contextFactory.CreateInstance()) {
        // Org itself
        if (_metadata == null || _metadata.Expires < DateTimeOffset.UtcNow) {
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

        if (_memberMetadata == null || _memberMetadata.Expires < DateTimeOffset.UtcNow) {
          // GitHub's `/orgs/<name>/members` endpoint does not provide role info for
          // each member.  To workaround, we make two requests and use the filter option
          // to only get admins or non-admins on each request.

          var updated = false;
          var newUsers = new List<gh.Account>();

          var members = await github.OrganizationMembers(_login, role: "member", cacheOptions: _memberMetadata);
          if (members.IsOk) {
            updated = true;
            _members = members.Result.Select(x => x.Id).ToHashSet();
            newUsers.AddRange(members.Result);
            this.Info($"Changed. Members: [{string.Join(",", _members.OrderBy(x => x))}]");
          } else if (!members.Succeeded) {
            throw new Exception($"Unexpected response: OrganizationMembers {members.Status}");
          }

          var admins = await github.OrganizationMembers(_login, role: "admin", cacheOptions: _adminMetadata);
          if (admins.IsOk) {
            updated = true;
            _admins = admins.Result.Select(x => x.Id).ToHashSet();
            newUsers.AddRange(admins.Result);
            this.Info($"Changed. Admins: [{string.Join(",", _admins.OrderBy(x => x))}]");
          } else if (!admins.Succeeded) {
            throw new Exception($"Unexpected response: OrganizationAdmins {admins.Status}");
          }

          if (updated) {
            changes.UnionWith(
              await context.BulkUpdateAccounts(
                members.Date,
                _mapper.Map<IEnumerable<AccountTableType>>(newUsers)));

            var orgMemberChanges = await context.SetOrganizationUsers(
                _orgId,
                _members.Select(x => Tuple.Create(x, false))
                  .Concat(_admins.Select(x => Tuple.Create(x, true))));

            if (!orgMemberChanges.IsEmpty) {
              // Check for subscription changes
              var subscription = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == _orgId);

              if (subscription?.State == SubscriptionState.Subscribed) {
                // If you belong to a paid organization, your personal subscription
                // is complimentary.  We need to add or remove the coupon for this
                // as membership changes.
                tasks.AddRange(orgMemberChanges.Users.Select(x => _queueClient.BillingUpdateComplimentarySubscription(x)));
              }
            }
            changes.UnionWith(orgMemberChanges);
          }

          _memberMetadata = GitHubMetadata.FromResponse(members);
          _adminMetadata = GitHubMetadata.FromResponse(admins);
        }

        // Projects
        changes.UnionWith(await UpdateProjects(context, github));

        // This is OK from a DB Connection prespective since it's right at the end.
        // If any active org members are admins, update webhooks
        // TODO: Does this really need to happen this often?
        var activeAdmins = await context.OrganizationAccounts
          .AsNoTracking()
          .Where(x => x.OrganizationId == _orgId
            && x.Admin == true
            && x.User.Token != null)
          .Select(x => x.UserId)
          .ToArrayAsync();

        if (activeAdmins.Any()) {
          var randmin = activeAdmins[_random.Next(activeAdmins.Length)];
          tasks.Add(_queueClient.AddOrUpdateOrgWebhooks(_orgId, randmin));
        }
      }

      // Send Changes.
      if (!changes.IsEmpty) {
        tasks.Add(_queueClient.NotifyChanges(changes));
      }

      // Await all outstanding operations.
      await Task.WhenAll(tasks);
    }

    private async Task<IChangeSummary> UpdateProjects(ShipHubContext context, IGitHubPoolable github) {
      var changes = new ChangeSummary();

      if (_projectMetadata == null || _projectMetadata.Expires < DateTimeOffset.UtcNow) {
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
  }
}
