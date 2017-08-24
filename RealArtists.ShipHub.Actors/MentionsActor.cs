namespace RealArtists.ShipHub.Actors {
  using System;
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

  public class MentionsActor : Grain, IMentionsActor {
    public static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SyncIdle = TimeSpan.FromSeconds(SyncDelay.TotalSeconds * 3);
    public const uint MentionNibblePages = 10;

    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    private long _userId;
    private IGitHubActor _github;

    // MetaData
    private GitHubMetadata _mentionMetadata;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;
    private DateTimeOffset _mentionSince;

    public MentionsActor(IMapper mapper, IGrainFactory grainFactory, IFactory<ShipHubContext> contextFactory, IShipHubQueueClient queueClient) {
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
         .SingleOrDefaultAsync(x => x.Id == _userId);
      }

      if (user == null) {
        throw new InvalidOperationException($"User {_userId} does not exist and cannot be activated.");
      }

      if (!user.Tokens.Any()) {
        throw new InvalidOperationException($"User {_userId} has an invalid token and cannot be activated.");
      }

      _github = _grainFactory.GetGrain<IGitHubActor>(user.Id);

      _mentionMetadata = user.MentionMetadata;
      _mentionSince = user.MentionSince ?? EpochUtility.EpochOffset;

      // Always sync while active
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
        await context.UpdateMetadata("Accounts", "MentionMetadataJson", _userId, _mentionMetadata);
      }
    }

    public Task Sync() {
      // Calls to sync just indicate interest in syncing.
      // Rather than sync here, we just ensure that a timer is registered.
      _lastSyncInterest = DateTimeOffset.UtcNow;

      return Task.CompletedTask;
    }

    private async Task SyncTimerCallback(object state = null) {
      if (DateTimeOffset.UtcNow.Subtract(_lastSyncInterest) > SyncIdle) {
        DeactivateOnIdle();
        return;
      }

      var metaDataMeaningfullyChanged = false;
      var updater = new DataUpdater(_contextFactory, _mapper);

      try {
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

      await updater.Changes.Submit(_queueClient);

      // Save changes
      if (metaDataMeaningfullyChanged) {
        await Save();
      }
    }
  }
}
