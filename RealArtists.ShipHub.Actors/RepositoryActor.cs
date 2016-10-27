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
  using Orleans;
  using QueueClient;

  public class RepositoryActor : Grain, IRepositoryActor {
    public static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SyncIdle = TimeSpan.FromSeconds(SyncDelay.TotalSeconds * 3);

    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    private long _repoId;
    private string _fullName;

    // Metadata
    private GitHubMetadata _metadata;
    private GitHubMetadata _assignableMetadata;
    private GitHubMetadata _labelMetadata;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;
    private HashSet<long> _interestedUserIds = new HashSet<long>();
    private Random _random = new Random();

    public RepositoryActor(IMapper mapper, IGrainFactory grainFactory, IFactory<ShipHubContext> contextFactory, IShipHubQueueClient queueClient) {
      _mapper = mapper;
      _grainFactory = grainFactory;
      _contextFactory = contextFactory;
      _queueClient = queueClient;
    }

    public override async Task OnActivateAsync() {
      using (var context = _contextFactory.CreateInstance()) {
        _repoId = this.GetPrimaryKeyLong();

        // Ensure this repository actually exists
        var repo = await context.Repositories.SingleOrDefaultAsync(x => x.Id == _repoId);

        if (repo == null) {
          throw new InvalidOperationException($"Repository {_repoId} does not exist and cannot be activated.");
        }

        _fullName = repo.FullName;
        _metadata = repo.Metadata;
        _assignableMetadata = repo.AssignableMetadata;
        _labelMetadata = repo.LabelMetadata;
      }

      await base.OnActivateAsync();
    }

    public override async Task OnDeactivateAsync() {
      using (var context = _contextFactory.CreateInstance()) {
        // I think all we need to persist is the metadata.
        await context.UpdateMetadata("Repositories", _repoId, _metadata);
        await context.UpdateMetadata("Repositories", "AssignableMetadataJson", _repoId, _assignableMetadata);
        await context.UpdateMetadata("Repositories", "LabelMetadataJson", _repoId, _labelMetadata);
      }

      // TODO: Look into how agressively Orleans deactivates "inactive" grains.
      // We may need to delay deactivation based on sync interest.

      await base.OnDeactivateAsync();
    }

    public Task Sync(long forUserId) {
      // For now, calls to sync just indicate interest in syncing.
      // Rather than sync here, we just ensure that a timer is registered.
      _lastSyncInterest = DateTimeOffset.UtcNow;

      _interestedUserIds.Add(forUserId);

      if (_syncTimer == null) {
        _syncTimer = RegisterTimer(SyncCallback, null, TimeSpan.Zero, SyncDelay);
      }

      return Task.CompletedTask;
    }

    private async Task<IGitHubActor> GetRandomGitHubActor() {
      using (var context = _contextFactory.CreateInstance()) {
        while (_interestedUserIds.Count > 0) {
          // The way the current caching code works, it will try to re-use an existing token
          // no matter which grain I pick. Pick one at random to use as a fallback.
          var userId = _interestedUserIds.ElementAt(_random.Next(_interestedUserIds.Count));

          // TODO: We can't switch GitHubActors to userId from access tokens fast enough.
          var token = await context.Accounts
            .Where(x => x.Id == userId)
            .Select(x => x.Token)
            .SingleOrDefaultAsync();

          if (token.IsNullOrWhiteSpace()) {
            _interestedUserIds.Remove(userId);
          } else {
            return _grainFactory.GetGrain<IGitHubActor>(token);
          }
        }
      }

      return null;
    }

    private long? GetRandomInterestedUserId() {
      if (_interestedUserIds.Count > 0) {
        return _interestedUserIds.ElementAt(_random.Next(_interestedUserIds.Count));
      }
      return null;
    }

    private async Task SyncCallback(object state) {
      if (DateTimeOffset.UtcNow.Subtract(_lastSyncInterest) > SyncIdle
        || _interestedUserIds.Count == 0) {
        DeactivateOnIdle();
        return;
      }

      var github = await GetRandomGitHubActor();
      if (github == null) {
        return;
      }

      var randomUserId = GetRandomInterestedUserId();
      if (github == null) {
        return;
      }

      var tasks = new List<Task>();
      var changes = new ChangeSummary();
      using (var context = _contextFactory.CreateInstance()) {
        // Update repo
        if (_metadata == null || _metadata.Expires < DateTimeOffset.UtcNow) {
          var repo = await github.Repository(_fullName, _metadata);

          if (repo.Status != HttpStatusCode.NotModified) {
            changes.UnionWith(
              await context.BulkUpdateRepositories(repo.Date, _mapper.Map<IEnumerable<RepositoryTableType>>(new { repo.Result }))
            );
          }

          // Don't update until saved.
          _metadata = GitHubMetadata.FromResponse(repo);
        }

        // Update Assignees
        if (_assignableMetadata == null || _assignableMetadata.Expires < DateTimeOffset.UtcNow) {
          var assignees = await github.Assignable(_fullName, _assignableMetadata);
          if (assignees.Status != HttpStatusCode.NotModified) {
            changes.UnionWith(
              await context.BulkUpdateAccounts(assignees.Date, _mapper.Map<IEnumerable<AccountTableType>>(assignees.Result)),
              await context.SetRepositoryAssignableAccounts(_repoId, assignees.Result.Select(x => x.Id))
            );
          }

          _assignableMetadata = GitHubMetadata.FromResponse(assignees);
        }

        // Update Labels
        if (_labelMetadata == null || _labelMetadata.Expires < DateTimeOffset.UtcNow) {
          var labels = await github.Labels(_fullName, _labelMetadata);
          if (labels.Status != HttpStatusCode.NotModified) {
            changes.UnionWith(
              await context.SetRepositoryLabels(
                _repoId,
                labels.Result.Select(x => new LabelTableType() {
                  ItemId = _repoId,
                  Color = x.Color,
                  Name = x.Name
                }))
            );
          }

          _labelMetadata = GitHubMetadata.FromResponse(labels);
        }

        // Update Milestones
        tasks.Add(_queueClient.SyncRepositoryMilestones(_repoId, randomUserId.Value));
      }

      // Send Changes.
      if (!changes.Empty) {
        tasks.Add(_queueClient.NotifyChanges(changes));
      }

      // Await all outstanding operations.
      await Task.WhenAll(tasks);
    }
  }
}
