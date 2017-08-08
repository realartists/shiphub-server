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
  using Orleans;
  using QueueClient;
  using g = Common.GitHub.Models;

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
    bool _syncRepos = false;
    bool _syncBillingState = true;

    // Local cache
    private object _cacheLock = new object();
    private SyncSettings _syncSettings;
    private ImmutableHashSet<long> _linkedRepos; // Repos from /user/repos
    private ImmutableDictionary<long, IOrganizationActor> _orgActors; // Orgs from /user/orgs
    private ImmutableDictionary<long, IRepositoryActor> _repoActors; // Repos we sync, from AccountSyncRepositories
    private ImmutableDictionary<long, GitHubMetadata> _includeRepoMetadata; // Metadata cache from AccountSyncRepositories

    public UserActor(IMapper mapper, IGrainFactory grainFactory, IFactory<ShipHubContext> contextFactory, IShipHubQueueClient queueClient) {
      _mapper = mapper;
      _grainFactory = grainFactory;
      _contextFactory = contextFactory;
      _queueClient = queueClient;
    }

    public override async Task OnActivateAsync() {
      // Set this first as subsequent calls require it.
      _userId = this.GetPrimaryKeyLong();

      // Ensure this user actually exists, and lookup their token.
      var user = await Initialize(_userId);

      if (user == null) {
        throw new InvalidOperationException($"User {_userId} does not exist and cannot be activated.");
      }

      if (!user.Tokens.Any()) {
        throw new InvalidOperationException($"User {_userId} has an invalid token and cannot be activated.");
      }

      _github = _grainFactory.GetGrain<IGitHubActor>(user.Id);

      _metadata = user.Metadata;
      _repoMetadata = user.RepositoryMetadata;
      _orgMetadata = user.OrganizationMetadata;

      // Migration Step
      bool needsMigration;
      lock (_cacheLock) {
        // No settings + linked repos + empty sync repos == MIGRATE!
        needsMigration = _linkedRepos.Any() && _syncSettings == null && !_repoActors.Any();
      }
      if (needsMigration) {
        await SyncRepositories();
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

    private async Task<User> Initialize(long userId) {
      User user;
      using (var context = _contextFactory.CreateInstance()) {
        user = await context.Users
         .AsNoTracking()
         .Include(x => x.Tokens)
         .Include(x => x.Settings)
         .Include(x => x.LinkedRepositories)
         .Include(x => x.SyncRepositories)
         .Include(x => x.AccountOrganizations)
         .SingleOrDefaultAsync(x => x.Id == _userId);
      }

      // User must be fully populated
      lock (_cacheLock) {
        _syncSettings = user.Settings?.SyncSettings;
        _linkedRepos = user.LinkedRepositories.Select(x => x.RepositoryId).ToImmutableHashSet();

        _orgActors = user.AccountOrganizations
        .ToImmutableDictionary(x => x.OrganizationId, x => _grainFactory.GetGrain<IOrganizationActor>(x.OrganizationId));

        _repoActors = user.SyncRepositories
          .ToImmutableDictionary(x => x.RepositoryId, x => _grainFactory.GetGrain<IRepositoryActor>(x.RepositoryId));

        _includeRepoMetadata = user.SyncRepositories
          .Where(x => !_linkedRepos.Contains(x.RepositoryId))
          .ToImmutableDictionary(x => x.RepositoryId, x => x.RepositoryMetadata);
      }

      return user;
    }

    /// <summary>
    /// Checks the user's repo list using the cached metadata.
    /// </summary>
    /// <returns>True if the user's linked repos were updated.</returns>
    private async Task<bool> RefreshLinkedRepos(DataUpdater updater) {
      // Must always run.
      // If you want to check poll interval, do it in the calling location.

      var metadataMeaningfullyChanged = false;
      var repos = await _github.Repositories(_repoMetadata, RequestPriority.Interactive);

      if (repos.IsOk) {
        metadataMeaningfullyChanged = true;
        await updater.SetUserRepositories(_userId, repos.Date, repos.Result);
        lock (_cacheLock) {
          _linkedRepos = repos.Result.Select(x => x.Id).ToImmutableHashSet();
        }
      }

      // Don't update until saved.
      _repoMetadata = GitHubMetadata.FromResponse(repos);

      return metadataMeaningfullyChanged;
    }

    /// <summary>
    /// Computes and saves the the DB the set of repos that should be synced for this user.
    /// Uses cached linked repos and settings.
    /// </summary>
    private async Task RefreshSyncRepos(DataUpdater updater) {
      IEnumerable<g.Repository> includeRepos = null;
      IDictionary<long, GitHubMetadata> combinedRepoMetadata = null;
      var date = DateTimeOffset.UtcNow; // Not *technically* correct, but probably ok

      // Cache current versions
      var linkedRepos = _linkedRepos;
      var includeRepoMetadata = _includeRepoMetadata;
      var settings = _syncSettings;

      if (settings?.Include.Any() == true) {
        // Request all "included" repos to verify access
        var repoReqs = settings.Include
          .Where(x => !linkedRepos.Contains(x)) // Exclude linked repos (access already known)
          .Select(x => (RepoId: x, Request: _github.Repository(x, _includeRepoMetadata.Val(x), RequestPriority.Interactive)))
          .ToArray();
        await Task.WhenAll(repoReqs.Select(x => x.Request));

        // Collect the "successful" responses
        // Check explicitly for 404, since 502s are so common :/
        var successful = repoReqs
          .Where(x => x.Request.Status == TaskStatus.RanToCompletion)
          .Where(x => x.Request.Result.Status != HttpStatusCode.NotFound)
          .ToArray();

        includeRepos = successful
          .Where(x => x.Request.Result.IsOk)
          .Select(x => x.Request.Result.Result)
          .ToArray();
        includeRepoMetadata = successful.ToImmutableDictionary(x => x.RepoId, x => GitHubMetadata.FromResponse(x.Request.Result));

        // now union/intersect all the things
        combinedRepoMetadata = new Dictionary<long, GitHubMetadata>();
        foreach (var repoId in settings.Include) {
          if (linkedRepos.Contains(repoId)) {
            combinedRepoMetadata.Add(repoId, null);
          } else if (includeRepoMetadata.ContainsKey(repoId)) {
            combinedRepoMetadata.Add(repoId, includeRepoMetadata[repoId]);
          }
          // else drop it
        }
      }

      await updater.UpdateAccountSyncRepositories(
        _userId,
        settings?.AutoTrack ?? true,
        date,
        includeRepos,
        combinedRepoMetadata,
        settings?.Exclude);
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

    public Task SyncBillingState() {
      // Important, but not interactive. Can wait for next sync cycle.
      _syncBillingState = true;
      return Sync();
    }

    public async Task SyncRepositories() {
      // This gets called
      // 1) When we know a repo has been added or deleted.
      // 2) When the user has changed their repo sync settings

      var updater = new DataUpdater(_contextFactory, _mapper);

      // TODO: Do less work
      await Initialize(_userId);  // Loads updated settings
      await RefreshLinkedRepos(updater);
      await RefreshSyncRepos(updater);
      await Initialize(_userId);  // Applies any changes.

      await Sync();
    }

    private async Task SyncTimerCallback(object state = null) {
      if (DateTimeOffset.UtcNow.Subtract(_lastSyncInterest) > SyncIdle) {
        DeactivateOnIdle();
        return;
      }

      var metaDataMeaningfullyChanged = false;
      var tasks = new List<Task>();
      var updater = new DataUpdater(_contextFactory, _mapper);

      try {
        // NOTE: The following requests are (relatively) infrequent and important for access control (repos/orgs)
        // Give them high priority.

        // User
        if (_metadata.IsExpired()) {
          var user = await _github.User(_metadata, RequestPriority.Interactive);

          if (user.IsOk) {
            metaDataMeaningfullyChanged = true;
            await updater.UpdateAccounts(user.Date, new[] { user.Result });
          }

          // Don't update until saved.
          _metadata = GitHubMetadata.FromResponse(user);
        }

        // Update this user's org memberships
        if (_orgMetadata.IsExpired()) {
          var orgs = await _github.OrganizationMemberships(cacheOptions: _orgMetadata, priority: RequestPriority.Interactive);

          if (orgs.IsOk) {
            metaDataMeaningfullyChanged = true;
            await updater.SetUserOrganizations(_userId, orgs.Date, orgs.Result);

            lock (_cacheLock) {
              _orgActors = orgs.Result
                .ToImmutableDictionary(x => x.Organization.Id, x => _grainFactory.GetGrain<IOrganizationActor>(x.Organization.Id));
            }

            // When this user's org membership changes, re-evaluate whether or not they
            // should have a complimentary personal subscription.
            tasks.Add(_queueClient.BillingUpdateComplimentarySubscription(_userId));

            // Also re-evaluate their synced repos
            _syncRepos = true;
          }

          // Don't update until saved.
          _orgMetadata = GitHubMetadata.FromResponse(orgs);
        }

        // Update this user's repo memberships
        if (_syncRepos || _repoMetadata.IsExpired()) {
          if (await RefreshLinkedRepos(updater)) {
            metaDataMeaningfullyChanged = true;
            await RefreshSyncRepos(updater);
            // The world may have changed. Re-initialize.
            await Initialize(_userId);
          }
          _syncRepos = false;
        }
      } catch (GitHubRateException) {
        // nothing to do
      }

      await updater.Changes.Submit(_queueClient);

      lock (_cacheLock) {
        // Sync repos
        if (_repoActors.Any()) {
          tasks.AddRange(_repoActors.Values.Select(x => x.Sync()));
        }

        // Sync orgs
        if (_orgActors.Any()) {
          tasks.AddRange(_orgActors.Values.Select(x => x.Sync()));
        }

        // Billing
        // Must come last since orgs can change above
        if (_syncBillingState) {
          tasks.Add(_queueClient.BillingGetOrCreatePersonalSubscription(_userId));

          if (_orgActors.Any()) {
            tasks.Add(_queueClient.BillingSyncOrgSubscriptionState(_orgActors.Keys, _userId));
          }

          _syncBillingState = false;
        }
      }


      // Save changes
      if (metaDataMeaningfullyChanged) {
        await Save();
      }

      // Await all outstanding operations.
      await Task.WhenAll(tasks);
    }
  }
}
