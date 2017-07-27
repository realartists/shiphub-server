namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
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
  using Orleans;
  using QueueClient;

  public class UserActor : Grain, IUserActor {
    public static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SyncIdle = TimeSpan.FromSeconds(SyncDelay.TotalSeconds * 3);

    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    private long _userId;
    private IGitHubActor _github;

    // MetaData
    private GitHubMetadata _metadata;
    private GitHubMetadata _repoMetadata;
    private GitHubMetadata _orgMetadata;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;
    bool _forceRepos = false;
    bool _syncBillingState = true;

    public UserActor(IMapper mapper, IGrainFactory grainFactory, IFactory<ShipHubContext> contextFactory, IShipHubQueueClient queueClient) {
      _mapper = mapper;
      _grainFactory = grainFactory;
      _contextFactory = contextFactory;
      _queueClient = queueClient;
    }

    public override async Task OnActivateAsync() {
      using (var context = _contextFactory.CreateInstance()) {
        _userId = this.GetPrimaryKeyLong();

        // Ensure this user actually exists, and lookup their token.
        var user = await context.Users
          .AsNoTracking()
          .Include(x => x.Tokens)
          .SingleOrDefaultAsync(x => x.Id == _userId);

        if (user == null) {
          throw new InvalidOperationException($"User {_userId} does not exist and cannot be activated.");
        }

        if (!user.Tokens.Any()) {
          throw new InvalidOperationException($"User {_userId} has an invalid token and cannot be activated.");
        }

        _metadata = user.Metadata;
        _repoMetadata = user.RepositoryMetadata;
        _orgMetadata = user.OrganizationMetadata;

        _github = _grainFactory.GetGrain<IGitHubActor>(user.Id);
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
        await context.UpdateMetadata("Accounts", _userId, _metadata);
        await context.UpdateMetadata("Accounts", "RepoMetadataJson", _userId, _repoMetadata);
        await context.UpdateMetadata("Accounts", "OrgMetadataJson", _userId, _orgMetadata);
      }
    }

    public Task SyncBillingState() {
      _syncBillingState = true;
      return Sync();
    }

    public Task SyncRepositories() {
      _forceRepos = true;
      return Sync();
    }

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

      var tasks = new List<Task>();
      var updater = new DataUpdater(_contextFactory, _mapper);

      try {
        // NOTE: The following requests are (relatively) infrequent and important for access control (repos/orgs)
        // Give them high priority.

        // User
        if (_metadata.IsExpired()) {
          var user = await _github.User(_metadata, RequestPriority.Interactive);

          if (user.IsOk) {
            await updater.UpdateAccounts(user.Date, new[] { user.Result });
          }

          // Don't update until saved.
          _metadata = GitHubMetadata.FromResponse(user);
        }

        if (_syncBillingState) {
          tasks.Add(_queueClient.BillingGetOrCreatePersonalSubscription(_userId));
        }

        // Update this user's org memberships
        if (_orgMetadata.IsExpired()) {
          var orgs = await _github.OrganizationMemberships(cacheOptions: _orgMetadata, priority: RequestPriority.Interactive);

          if (orgs.IsOk) {
            await updater.SetUserOrganizations(_userId, orgs.Date, orgs.Result);

            // When this user's org membership changes, re-evaluate whether or not they
            // should have a complimentary personal subscription.
            tasks.Add(_queueClient.BillingUpdateComplimentarySubscription(_userId));
          }

          _orgMetadata = GitHubMetadata.FromResponse(orgs);
        }

        // Update this user's repo memberships
        if (_forceRepos || _repoMetadata.IsExpired()) {
          var repos = await _github.Repositories(_repoMetadata, RequestPriority.Interactive);

          if (repos.IsOk) {
            var keepRepos = repos.Result.Where(x => x.HasIssues && x.Permissions.Push);
            await updater.SetUserRepositories(_userId, repos.Date, keepRepos);
          }

          // Don't update until saved.
          _repoMetadata = GitHubMetadata.FromResponse(repos);
          _forceRepos = false;
        }
      } catch (GitHubRateException) {
        // nothing to do
      }

      await updater.Changes.Submit(_queueClient);

      // TODO: Save the repo list locally in this object
      // TODO: Actually and load and maintain the list of orgs inside the object
      // Maintain the grain references too.

      long[] allRepoIds;
      long[] allOrgIds;
      HashSet<long> orgsToSync;
      using (var context = _contextFactory.CreateInstance()) {
        var allRepos = await context.AccountRepositories
          .AsNoTracking()
          .Where(x => x.AccountId == _userId)
          .Select(x => new { RepositoryId = x.RepositoryId, AccountId = x.Repository.AccountId })
          .ToArrayAsync();
        allRepoIds = allRepos.Select(r => r.RepositoryId).ToArray();

        orgsToSync = allRepos.Select(x => x.AccountId).ToHashSet(); // Accounts with accessible repos
        allOrgIds = await context.OrganizationAccounts
          .AsNoTracking()
          .Where(x => x.UserId == _userId)
          .Select(x => x.OrganizationId)
          .ToArrayAsync();
      }

      if (allRepoIds.Any()) {
        tasks.AddRange(allRepoIds.Select(x => _grainFactory.GetGrain<IRepositoryActor>(x).Sync()));
      }
      
      orgsToSync.IntersectWith(allOrgIds);

      if (orgsToSync.Any()) {
        tasks.AddRange(allOrgIds.Select(x => _grainFactory.GetGrain<IOrganizationActor>(x).Sync()));
        if (_syncBillingState) {
          tasks.Add(_queueClient.BillingSyncOrgSubscriptionState(orgsToSync, _userId));
        }
      }

      // Save changes
      await Save();
      // Await all outstanding operations.
      await Task.WhenAll(tasks);

      _syncBillingState = false;
    }
  }
}
