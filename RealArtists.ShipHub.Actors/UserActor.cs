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
  using Orleans;
  using QueueClient;
  using g = Common.GitHub.Models;

  public class UserActor : Grain, IUserActor {
    public static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SyncIdle = TimeSpan.FromSeconds(SyncDelay.TotalSeconds * 3);
    public const uint MentionNibblePages = 20;

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
    private GitHubMetadata _mentionMetadata;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;
    bool _syncLinkedRepos = false;
    bool _syncSyncRepos = false;
    bool _syncBillingState = true;
    private DateTimeOffset _mentionSince;

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

      _github = _grainFactory.GetGrain<IGitHubActor>(user.Id);

      _metadata = user.Metadata;
      _repoMetadata = user.RepositoryMetadata;
      _orgMetadata = user.OrganizationMetadata;
      _mentionMetadata = user.MentionMetadata;

      _mentionSince = user.MentionSince ?? EpochUtility.EpochOffset;

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
        _syncLinkedRepos = true;
        _syncSyncRepos = true;
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
        await context.UpdateMetadata("Accounts", "MentionMetadataJson", _userId, _mentionMetadata);
      }
    }

    public async Task SetSyncSettings(SyncSettings syncSettings) {
      using (var context = _contextFactory.CreateInstance()) {
        await context.SetAccountSettings(_userId, syncSettings);
      }
      _syncSettings = syncSettings;
      _syncLinkedRepos = true;
      _syncSyncRepos = true;
      await InternalSync();
    }

    public Task Sync() {
      // Calls to sync just indicate interest in syncing.
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

    public Task SyncRepositories() {
      // This gets called when we know a repo has been added or deleted.
      _syncLinkedRepos = true;
      return InternalSync();
    }

    private async Task SyncTimerCallback(object state = null) {
      if (DateTimeOffset.UtcNow.Subtract(_lastSyncInterest) > SyncIdle) {
        DeactivateOnIdle();
        return;
      }

      await this.AsReference<IUserActor>().InternalSync();
    }

    public async Task InternalSync() {
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

            _orgActors = orgs.Result
              .ToDictionary(x => x.Organization.Id, x => _grainFactory.GetGrain<IOrganizationActor>(x.Organization.Id));

            // When this user's org membership changes, re-evaluate whether or not they
            // should have a complimentary personal subscription.
            tasks.Add(_queueClient.BillingUpdateComplimentarySubscription(_userId));

            // Also re-evaluate their synced repos
            _syncLinkedRepos = true;
          }

          // Don't update until saved.
          _orgMetadata = GitHubMetadata.FromResponse(orgs);
        }

        // Update this user's repo memberships
        if (_syncLinkedRepos || _repoMetadata.IsExpired()) {
          var repos = await _github.Repositories(_repoMetadata, RequestPriority.Interactive);

          if (repos.IsOk) {
            metaDataMeaningfullyChanged = true;
            _syncSyncRepos = true;

            await updater.SetUserRepositories(_userId, repos.Date, repos.Result);

            _linkedRepos = repos.Result.Select(x => x.Id).ToHashSet();
            // don't update _repoActors yet
          }

          // Don't update until saved.
          _repoMetadata = GitHubMetadata.FromResponse(repos);
          _syncLinkedRepos = false;
        }

        // Update this user's sync repos
        if (_syncSyncRepos) {
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

          _repoActors = syncRepoMetadata
            .ToDictionary(x => x.Key, x => _grainFactory.GetGrain<IRepositoryActor>(x.Key));

          _includeRepoMetadata = syncRepoMetadata
            .Where(x => !_linkedRepos.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value);

          // TODO: Actually detect changes.
          var cs = new ChangeSummary();
          cs.Add(userId: _userId);
          updater.UnionWithExternalChanges(cs);

          _syncSyncRepos = false;
        }

        // Issue Mentions
        if (_mentionMetadata.IsExpired()) {
          var mentions = await _github.IssueMentions(_mentionSince, MentionNibblePages, _mentionMetadata, RequestPriority.Background);

          if (mentions.IsOk && mentions.Result.Any()) {
            metaDataMeaningfullyChanged = true;

            await updater.UpdateIssueMentions(_userId, mentions.Result);

            var maxSince = mentions.Result.Max(x => x.UpdatedAt).AddSeconds(-5);
            if (maxSince != _mentionSince) {
              await updater.UpdateAccountMentionSince(_userId, maxSince);
              _mentionSince = maxSince;
            }
          }

          // Don't update until saved.
          _mentionMetadata = GitHubMetadata.FromResponse(mentions);
        }
      } catch (GitHubRateException) {
        // nothing to do
      }

      await updater.Changes.Submit(_queueClient, urgent: true);

      // Save changes
      if (metaDataMeaningfullyChanged) {
        await Save();
      }

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

      // Await all outstanding operations.
      await Task.WhenAll(tasks);
    }
  }
}
