namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces;
  using ActorInterfaces.GitHub;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Orleans;
  using Orleans.Concurrency;
  using QueueClient;
  using g = Common.GitHub.Models;

  [Reentrant]
  public class UserActor : Grain, IUserActor, IDisposable {
    public static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SyncIdle = TimeSpan.FromSeconds(SyncDelay.TotalSeconds * 3);
    public const uint MentionNibblePages = 10;

    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    private long _userId;
    private string _userInfo;
    private IGitHubActor _github;
    private IMentionsActor _mentions;

    private SemaphoreSlim _syncLimit = new SemaphoreSlim(1); // Only allow one sync at a time

    // MetaData
    private GitHubMetadata _metadata;
    private GitHubMetadata _repoMetadata;
    private GitHubMetadata _orgMetadata;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;
    int _linkedReposCurrent = 0;
    int _linkedReposDesired = 0;
    int _syncReposCurrent = 0;
    int _syncReposDesired = 0;
    int _billingStateCurrent = 0;
    int _billingStateDesired = 0;

    // Local cache
    private SyncSettings _syncSettings;
    private HashSet<long> _linkedRepos; // Repos from /user/repos
    private Dictionary<long, IOrganizationActor> _orgActors; // Orgs from /user/orgs
    private Dictionary<long, IRepositoryActor> _repoActors; // Repos we sync, from AccountSyncRepositories
    private Dictionary<long, GitHubMetadata> _includeRepoMetadata; // Metadata cache from AccountSyncRepositories

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
      User user = null;
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

      if (user == null) {
        throw new InvalidOperationException($"User {_userId} does not exist and cannot be activated.");
      }

      if (!user.Tokens.Any()) {
        throw new InvalidOperationException($"User {_userId} has an invalid token and cannot be activated.");
      }

      _userInfo = $"{user.Login} ({user.Id})";

      _github = _grainFactory.GetGrain<IGitHubActor>(user.Id);
      _mentions = _grainFactory.GetGrain<IMentionsActor>(user.Id);

      _metadata = user.Metadata;
      _repoMetadata = user.RepositoryMetadata;
      _orgMetadata = user.OrganizationMetadata;

      _syncSettings = user.Settings?.SyncSettings;
      _linkedRepos = user.LinkedRepositories.Select(x => x.RepositoryId).ToHashSet();

      _orgActors = user.AccountOrganizations
      .ToDictionary(x => x.OrganizationId, x => _grainFactory.GetGrain<IOrganizationActor>(x.OrganizationId));

      _repoActors = user.SyncRepositories
        .ToDictionary(x => x.RepositoryId, x => _grainFactory.GetGrain<IRepositoryActor>(x.RepositoryId));

      _includeRepoMetadata = user.SyncRepositories
        .Where(x => !_linkedRepos.Contains(x.RepositoryId))
        .ToDictionary(x => x.RepositoryId, x => x.RepositoryMetadata);

      // Migration Step
      // No settings + linked repos + empty sync repos == MIGRATE!
      if (_linkedRepos.Any() && _syncSettings == null && !_repoActors.Any()) {
        Interlocked.Increment(ref _linkedReposDesired);
        Interlocked.Increment(ref _syncReposDesired);
      }

      // We always want to sync while the UserActor is loaded;
      _lastSyncInterest = DateTimeOffset.UtcNow;
      _syncTimer = RegisterTimer(SyncTimerCallback, null, TimeSpan.Zero, SyncDelay);

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

    public async Task SetSyncSettings(SyncSettings syncSettings) {
      await _syncLimit.WaitAsync();
      try {
        using (var context = _contextFactory.CreateInstance()) {
          await context.SetAccountSettings(_userId, syncSettings);
        }

        _syncSettings = syncSettings;

        Interlocked.Increment(ref _linkedReposDesired);
        Interlocked.Increment(ref _syncReposDesired);
        await InternalSync();
      } finally {
        _syncLimit.Release();
      }
    }

    public Task Sync() {
      // Calls to sync just indicate interest in syncing.
      // Rather than sync here, we just ensure that a timer is registered.
      _lastSyncInterest = DateTimeOffset.UtcNow;

      return Task.CompletedTask;
    }

    public Task SyncBillingState() {
      // Important, but not interactive. Can wait for next sync cycle.
      Interlocked.Increment(ref _billingStateDesired);
      return Sync();
    }

    public async Task SyncRepositories() {
      await _syncLimit.WaitAsync();
      try {
        // This gets called when we know a repo has been added or deleted.
        Interlocked.Increment(ref _linkedReposDesired);
        await InternalSync();
      } finally {
        _syncLimit.Release();
      }
    }

    private async Task SyncTimerCallback(object state = null) {
      if (DateTimeOffset.UtcNow.Subtract(_lastSyncInterest) > SyncIdle) {
        DeactivateOnIdle();
        return;
      }

      var metaDataMeaningfullyChanged = false;
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
            // Unlike orgs, login renames are fine here.
            // Current user is implicit in all calls, not specified.
          }

          // Don't update until saved.
          _metadata = GitHubMetadata.FromResponse(user);
        }

        await _syncLimit.WaitAsync();
        try {
          metaDataMeaningfullyChanged |= await InternalSync(updater);

          // Billing
          // Must come last since orgs can change above
          var savedBillingCurrent = _billingStateCurrent;
          var savedBillingDesired = _billingStateDesired;
          if (savedBillingCurrent < savedBillingDesired) {
            _queueClient.BillingGetOrCreatePersonalSubscription(_userId).LogFailure(_userInfo);

            if (_orgActors.Any()) {
              _queueClient.BillingSyncOrgSubscriptionState(_orgActors.Keys, _userId).LogFailure(_userInfo);
            }

            Interlocked.CompareExchange(ref _billingStateCurrent, savedBillingDesired, savedBillingCurrent);
          }
        } finally {
          _syncLimit.Release();
        }
      } catch (GitHubRateException) {
        // nothing to do
      }

      await updater.Changes.Submit(_queueClient, urgent: true);

      // Mentions
      _mentions.Sync().LogFailure(_userInfo);

      // Save changes
      if (metaDataMeaningfullyChanged) {
        await Save();
      }
    }

    public async Task InternalSync() {
      // It's gross that this exists. oh well.
      var metaDataMeaningfullyChanged = false;
      var updater = new DataUpdater(_contextFactory, _mapper);

      try {
        metaDataMeaningfullyChanged = await InternalSync(updater);
      } catch (GitHubRateException) {
        // nothing to do
      }

      await updater.Changes.Submit(_queueClient, urgent: true);

      // Save changes
      if (metaDataMeaningfullyChanged) {
        await Save();
      }
    }

    public async Task<bool> InternalSync(DataUpdater updater) {
      if (_syncLimit.CurrentCount != 0) {
        throw new InvalidOperationException($"{nameof(InternalSync)} requires the sync semaphore be held.");
      }

      var metaDataMeaningfullyChanged = false;

      try {
        // NOTE: The following requests are (relatively) infrequent and important for access control (repos/orgs)
        // Give them high priority.

        // Update this user's org memberships
        if (_orgMetadata.IsExpired()) {
          var orgs = await _github.OrganizationMemberships(cacheOptions: _orgMetadata, priority: RequestPriority.Interactive);

          if (orgs.IsOk) {
            metaDataMeaningfullyChanged = true;
            await updater.SetUserOrganizations(_userId, orgs.Date, orgs.Result);

            _orgActors = orgs.Result
              .ToDictionary(x => x.Organization.Id, x => _grainFactory.GetGrain<IOrganizationActor>(x.Organization.Id));

            // When this user's org membership changes, re-evaluate whether or not they
            // should have a complimentary personal subscription.
            _queueClient.BillingUpdateComplimentarySubscription(_userId).LogFailure(_userInfo);

            // Also re-evaluate their linked repos
            Interlocked.Increment(ref _linkedReposDesired);
          }

          // Don't update until saved.
          _orgMetadata = GitHubMetadata.FromResponse(orgs);
        }

        // Update this user's repo memberships
        var savedLinkCurrent = _linkedReposCurrent;
        var savedLinkDesired = _linkedReposDesired;

        if ((savedLinkCurrent < savedLinkDesired) || _repoMetadata.IsExpired()) {
          var repos = await _github.Repositories(_repoMetadata, RequestPriority.Interactive);

          if (repos.IsOk) {
            metaDataMeaningfullyChanged = true;
            Interlocked.Increment(ref _syncReposDesired);

            await updater.SetUserRepositories(_userId, repos.Date, repos.Result);

            _linkedRepos = repos.Result.Select(x => x.Id).ToHashSet();
            // don't update _repoActors yet
          }

          // Don't update until saved.
          _repoMetadata = GitHubMetadata.FromResponse(repos);
          Interlocked.CompareExchange(ref _linkedReposCurrent, savedLinkDesired, savedLinkCurrent);
        }

        // Update this user's sync repos
        var savedReposCurrent = _syncReposCurrent;
        var savedReposDesired = _syncReposDesired;
        if (savedReposCurrent < savedReposDesired) {
          IEnumerable<g.Repository> updateRepos = null;
          IDictionary<long, GitHubMetadata> combinedRepoMetadata = null;
          var date = DateTimeOffset.UtcNow; // Not *technically* correct, but probably ok

          if (_syncSettings?.Include.Any() == true) {
            // Request all "included" repos to verify access
            var repoReqs = _syncSettings.Include
              .Where(x => !_linkedRepos.Contains(x)) // Exclude linked repos (access already known)
              .Select(x => (RepoId: x, Request: _github.Repository(x, _includeRepoMetadata.Val(x), RequestPriority.Interactive)))
              .ToArray();
            await Task.WhenAll(repoReqs.Select(x => x.Request));

            // Collect the "successful" responses
            // Check explicitly for 404, since 502s are so common :/
            var successful = repoReqs
              .Where(x => x.Request.Status == TaskStatus.RanToCompletion)
              .Where(x => x.Request.Result.Status != HttpStatusCode.NotFound)
              .ToArray();

            _includeRepoMetadata = successful.ToDictionary(x => x.RepoId, x => GitHubMetadata.FromResponse(x.Request.Result));

            updateRepos = successful
              .Where(x => x.Request.Result.IsOk)
              .Select(x => x.Request.Result.Result)
              .ToArray();

            // now union/intersect all the things
            combinedRepoMetadata = new Dictionary<long, GitHubMetadata>();
            foreach (var repoId in _syncSettings.Include) {
              if (_linkedRepos.Contains(repoId)) {
                combinedRepoMetadata.Add(repoId, null);
              } else if (_includeRepoMetadata.ContainsKey(repoId)) {
                combinedRepoMetadata.Add(repoId, _includeRepoMetadata[repoId]);
              }
              // else drop it
            }
          }

          var syncRepoMetadata = await updater.UpdateAccountSyncRepositories(
            _userId,
            _syncSettings?.AutoTrack ?? true,
            date,
            updateRepos,
            combinedRepoMetadata,
            _syncSettings?.Exclude);

          _repoActors = syncRepoMetadata.Keys
            .ToDictionary(x => x, x => _grainFactory.GetGrain<IRepositoryActor>(x));

          _includeRepoMetadata = syncRepoMetadata
            .Where(x => !_linkedRepos.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value);

          // TODO: Actually detect changes.
          var cs = new ChangeSummary();
          cs.Add(userId: _userId);
          updater.UnionWithExternalChanges(cs);

          Interlocked.CompareExchange(ref _syncReposCurrent, savedReposDesired, savedReposCurrent);
        }
      } catch (GitHubRateException) {
        // nothing to do
      }

      // We do this here so newly added repos and orgs sync immediately

      // Sync repos
      foreach (var repo in _repoActors.Values) {
        repo.Sync().LogFailure(_userInfo);
      }

      // Sync orgs
      foreach (var org in _orgActors.Values) {
        org.Sync().LogFailure(_userInfo);
      }

      return metaDataMeaningfullyChanged;
    }

    ////////////////////////////////////////////////////////////
    // IDisposable
    ////////////////////////////////////////////////////////////

    private bool disposedValue = false;
    protected virtual void Dispose(bool disposing) {
      if (!disposedValue) {
        if (disposing) {
          if (_syncLimit != null) {
            _syncLimit.Dispose();
            _syncLimit = null;
          }
        }
        disposedValue = true;
      }
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }
  }
}
