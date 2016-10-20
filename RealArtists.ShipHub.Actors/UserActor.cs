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

  public class UserActor : Grain, IUserActor {
    public static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SyncIdle = TimeSpan.FromSeconds(SyncDelay.TotalSeconds * 3);

    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    private long _userId;
    private string _login;
    private IGitHubActor _github;

    // MetaData
    private GitHubMetadata _metadata;
    private GitHubMetadata _repoMetadata;
    private GitHubMetadata _orgMetadata;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;

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
        var user = await context.Users.SingleAsync(x => x.Id == _userId);

        if (user == null) {
          throw new InvalidOperationException($"User {_userId} does not exists and cannot be activated.");
        }

        if (user.Token.IsNullOrWhiteSpace()) {
          throw new InvalidOperationException($"User {_userId} has an invalid token and cannot be activated.");
        }

        _login = user.Login;
        _metadata = user.Metadata;
        _repoMetadata = user.RepositoryMetadata;
        _orgMetadata = user.OrganizationMetadata;

        _github = _grainFactory.GetGrain<IGitHubActor>(user.Token);
      }

      await base.OnActivateAsync();
    }

    public override Task OnDeactivateAsync() {
      // TODO: Persist anything stored in memory we want to reload later.
      // Ex: Metadata, sync progress, etc.
      using (var context = _contextFactory.CreateInstance()) {
        //context.UpdateMetadata("", _userId, 
      }

      // TODO: Look into how agressively Orleans deactivates "inactive" grains.
      // We may need to delay deactivation based on sync interest.

      return base.OnDeactivateAsync();
    }

    public Task Sync() {
      // For now, calls to sync just indicate interest in syncing.
      // Rather than sync here, we just ensure that a timer is registered.
      _lastSyncInterest = DateTimeOffset.UtcNow;

      if (_syncTimer == null) {
        _syncTimer = RegisterTimer(SyncCallback, null, TimeSpan.Zero, SyncDelay);
      }

      return Task.CompletedTask;
    }

    public Task ForceSyncRepositories() {
      return TimerSync(forceRepos: false);
    }

    public async Task InvalidateToken(string token) {
      using (var context = _contextFactory.CreateInstance()) {
        await context.RevokeAccessToken(token);

        // TODO: Clear cache metadata tied to this token
        if (_metadata.AccessToken == token) {
          _metadata = null;
        }

        if (_repoMetadata.AccessToken == token) {
          _repoMetadata = null;
        }

        if (_orgMetadata.AccessToken == token) {
          _orgMetadata = null;
        }

        // Clear current GitHubClient reference if its token matches
        // Deactivate sync and the grain itself
        if (_github.GetPrimaryKeyString() == token) {
          _github = null;

          _syncTimer?.Dispose();
          _syncTimer = null;

          DeactivateOnIdle();
        }
      }
    }

    public Task UpdateToken(string token) {
      // Right now token updates are only handled by login.
      // TODO: Update login to call this

      // If token does not match current, replace GitHubActor reference.
      if (_github.GetPrimaryKeyString() != token) {
        _github = _grainFactory.GetGrain<IGitHubActor>(token);
      }

      return Task.CompletedTask;
    }

    // Implementation methods

    private async Task SyncCallback(object state) {
      if (DateTimeOffset.UtcNow.Subtract(_lastSyncInterest) > SyncIdle) {
        DeactivateOnIdle();
        return;
      }

      await TimerSync(forceRepos: false);
    }

    private async Task TimerSync(bool forceRepos) {
      if (_github == null) {
        return;
      }

      var tasks = new List<Task>();
      var changes = new ChangeSummary();
      using (var context = _contextFactory.CreateInstance()) {

        // User
        if (_metadata == null || _metadata.Expires < DateTimeOffset.UtcNow) {
          var user = await _github.User(_metadata);

          if (user.Status != HttpStatusCode.NotModified) {
            changes.UnionWith(
              await context.UpdateAccount(user.Date, _mapper.Map<AccountTableType>(user.Result))
            );
          }

          // Don't update until saved.
          _metadata = GitHubMetadata.FromResponse(user);
        }

        // TODO: Don't think this should actually happen this often
        tasks.Add(_queueClient.BillingGetOrCreatePersonalSubscription(_userId));

        // Update this user's org memberships
        if (_orgMetadata == null || _orgMetadata.Expires < DateTimeOffset.UtcNow) {
          var orgs = await _github.OrganizationMemberships(cacheOptions: _orgMetadata);

          if (orgs.Status != HttpStatusCode.NotModified) {
            changes.UnionWith(
              await context.BulkUpdateAccounts(orgs.Date, _mapper.Map<IEnumerable<AccountTableType>>(orgs.Result.Select(x => x.Organization)))
            );

            var userOrgChanges = await context.SetUserOrganizations(_userId, orgs.Result.Select(x => x.Organization.Id));
            changes.UnionWith(userOrgChanges);

            if (!userOrgChanges.Empty) {
              // When this user's org membership changes, re-evaluate whether or not they
              // should have a complimentary personal subscription.
              tasks.Add(_queueClient.BillingUpdateComplimentarySubscription(_userId));
            }
          }

          _orgMetadata = GitHubMetadata.FromResponse(orgs);
        }

        // TODO: Actually and load and maintain the list of orgs inside the object
        var allOrgIds = await context.OrganizationAccounts
          .Where(x => x.UserId == _userId)
          .Select(x => x.OrganizationId)
          .ToArrayAsync();

        if (allOrgIds.Any()) {
          tasks.AddRange(allOrgIds.Select(x => _queueClient.SyncOrganizationMembers(x, _userId)));
          tasks.AddRange(allOrgIds.Select(x => _queueClient.BillingSyncOrgSubscriptionState(x, _userId)));
        }

        // Update this user's repo memberships
        if (forceRepos || _repoMetadata == null || _repoMetadata.Expires < DateTimeOffset.UtcNow) {
          var repos = await _github.Repositories(_repoMetadata);

          if (repos.Status != HttpStatusCode.NotModified) {
            var reposWithIssues = repos.Result.Where(x => x.HasIssues);
            var assignableRepos = reposWithIssues.ToDictionary(x => x.FullName, x => _github.IsAssignable(x.FullName, _login));
            await Task.WhenAll(assignableRepos.Values);
            var keepRepos = reposWithIssues.Where(x => assignableRepos[x.FullName].Result.Result).ToArray();

            var owners = keepRepos
              .Select(x => x.Owner)
              .Distinct(x => x.Login);

            changes.UnionWith(
              await context.BulkUpdateAccounts(repos.Date, _mapper.Map<IEnumerable<AccountTableType>>(owners)),
              await context.BulkUpdateRepositories(repos.Date, _mapper.Map<IEnumerable<RepositoryTableType>>(keepRepos)),
              await context.SetAccountLinkedRepositories(_userId, keepRepos.Select(x => Tuple.Create(x.Id, x.Permissions.Admin)))
            );
          }

          // Don't update until saved.
          _repoMetadata = GitHubMetadata.FromResponse(repos);
        }

        // TODO: Save the repo list locally in this object
        var allRepos = await context.AccountRepositories
          .Where(x => x.AccountId == _userId)
          .ToArrayAsync();

        tasks.AddRange(allRepos.Select(x => _queueClient.SyncRepository(x.RepositoryId, _userId)));
        tasks.AddRange(allRepos
          .Where(x => x.Admin)
          .Select(x => _queueClient.AddOrUpdateRepoWebhooks(x.RepositoryId, _userId)));
      }

      // Send Changes.
      if (!changes.Empty) {
        tasks.Add(_queueClient.NotifyChanges(changes));
      }

      // Await all outstanding operations.
      await Task.WhenAll(tasks);

      // Future Compatibility:

      // Tell the user's orgs and repos that we're interested
      // TODO: Sync orgs
      // TODO: Sync repos
    }
  }
}
