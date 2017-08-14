namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
  using System.Collections.Immutable;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Text;
  using System.Text.RegularExpressions;
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

  public class RepositoryActor : Grain, IRepositoryActor {
    private const int BiteChunkPages = 75;
    private const int NibbleChunkPages = 25;
    private const int PullRequestUpdateChunkSize = 5;

    public static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan HookErrorDelay = TimeSpan.FromHours(12);
    public static readonly TimeSpan SyncIdle = TimeSpan.FromSeconds(SyncDelay.TotalSeconds * 3);
    public static readonly TimeSpan SyncIssueTemplateHysteresis = TimeSpan.FromSeconds(2);
    public static readonly int PollIssueTemplateSkip = 5; // If we have to poll the ISSUE_TEMPLATE, do it every N Syncs
    public static readonly int SaveSkip = 5; // Saving metadata is expensive

    public static ImmutableHashSet<string> RequiredEvents { get; } = ImmutableHashSet.Create(
      "commit_comment"
      , "issue_comment"
      , "issues"
      , "label"
      , "milestone"
      , "pull_request_review_comment"
      , "pull_request_review"
      , "pull_request"
      , "push"
      , "repository"
      , "status"
    );

    public static Regex ExactMatchIssueTemplateRegex { get; } = new Regex(
      @"^issue_template(?:\.\w+)?$",
      RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
      TimeSpan.FromMilliseconds(200));

    public static Regex EndsWithIssueTemplateRegex { get; } = new Regex(
      @"issue_template(?:\.\w+)?$",
      RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
      TimeSpan.FromMilliseconds(200));

    public static Regex ExactMatchPullRequestTemplateRegex { get; } = new Regex(
      @"^pull_request_template(?:\.\w+)?$",
      RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
      TimeSpan.FromMilliseconds(200));

    public static Regex EndsWithPullRequestTemplateRegex { get; } = new Regex(
      @"pull_request_template(?:\.\w+)?$",
      RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
      TimeSpan.FromMilliseconds(200));

    // Constructor parameters
    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;
    private string _apiHostName;

    // Repo properties
    private long _repoId;
    private string _fullName;
    private bool _disabled;
    private bool _hasProjects;
    private bool _isPrivate;

    private long _repoSize;
    private string _issueTemplateContent;
    private string _pullRequestTemplateContent;

    // Metadata
    private GitHubMetadata _metadata;
    private GitHubMetadata _assignableMetadata;
    private GitHubMetadata _commentMetadata;
    private GitHubMetadata _issueMetadata;
    private GitHubMetadata _labelMetadata;
    private GitHubMetadata _milestoneMetadata;
    private GitHubMetadata _pullRequestMetadata;
    private GitHubMetadata _projectMetadata;

    private GitHubMetadata _contentsRootMetadata;
    private GitHubMetadata _contentsDotGithubMetadata;
    private GitHubMetadata _contentsIssueTemplateMetadata;
    private GitHubMetadata _contentsPullRequestTemplateMetadata;

    // Chunk tracking
    private DateTimeOffset _commentSince;
    private DateTimeOffset _issueSince;
    private uint _pullRequestSkip;
    private DateTimeOffset? _pullRequestUpdatedAt;

    // Idle state tracking
    bool _idle = false;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;
    private IDisposable _syncIssueTemplateTimer;
    private bool _needsIssueTemplateSync;
    private bool _pollIssueTemplate;
    private int _syncCount;
    private bool _issuesFullyImported;
    private bool _needsForceResyncIssues;

    private IDictionary<string, GitHubMetadata> _protectedBranchMetadata;

    public RepositoryActor(
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
        var repoId = this.GetPrimaryKeyLong();

        // Ensure this repository actually exists
        var repo = await context.Repositories.SingleOrDefaultAsync(x => x.Id == repoId);

        if (repo == null) {
          throw new InvalidOperationException($"Repository {repoId} does not exist and cannot be activated.");
        }

        // Core properties
        Initialize(repo.Id, repo.FullName);
        _disabled = repo.Disabled;
        _hasProjects = repo.HasProjects;
        _isPrivate = repo.Private;

        // Repo metadata
        _metadata = repo.Metadata;
        _assignableMetadata = repo.AssignableMetadata;
        _commentMetadata = repo.CommentMetadata;
        _issueMetadata = repo.IssueMetadata;
        _labelMetadata = repo.LabelMetadata;
        _milestoneMetadata = repo.MilestoneMetadata;
        _projectMetadata = repo.ProjectMetadata;
        _pullRequestMetadata = repo.PullRequestMetadata; // Null signals walk in creation asc order, non-null signals walk in updated desc order
        _contentsRootMetadata = repo.ContentsRootMetadata;
        _contentsDotGithubMetadata = repo.ContentsDotGitHubMetadata;
        _contentsIssueTemplateMetadata = repo.ContentsIssueTemplateMetadata;
        _contentsPullRequestTemplateMetadata = repo.ContentsPullRequestTemplateMetadata;

        // Issue sync state
        _issueSince = repo.IssueSince ?? EpochUtility.EpochOffset; // Reasonable default.
        _issuesFullyImported = repo.IssuesFullyImported;

        // PR sync state
        _pullRequestSkip = (uint)(repo.PullRequestSkip ?? 0);
        _pullRequestUpdatedAt = repo.PullRequestUpdatedAt;

        // Comment sync state
        _commentSince = repo.CommentSince ?? EpochUtility.EpochOffset;

        // Issue and PR template sync state
        _repoSize = repo.Size;
        _issueTemplateContent = repo.IssueTemplate;
        _pullRequestTemplateContent = repo.PullRequestTemplate;

        // if we have no webhook, we must poll the ISSUE_TEMPLATE
        _pollIssueTemplate = await context.Hooks.Where(hook => hook.RepositoryId == _repoId && hook.LastSeen != null).AnyAsync();
        _needsIssueTemplateSync = _contentsRootMetadata == null;
        this.Info($"{_fullName} polls ISSUE_TEMPLATE:{_pollIssueTemplate}");

        _protectedBranchMetadata = await context.ProtectedBranches
          .Where(x => x.RepositoryId == repo.Id)
          .ToDictionaryAsync(k => k.Name, v => v.Metadata);
      }

      await base.OnActivateAsync();
    }

    public void Initialize(long repoId, string fullName) {
      _repoId = repoId;
      _fullName = fullName;
    }

    public override async Task OnDeactivateAsync() {
      _syncTimer?.Dispose();
      _syncTimer = null;

      _syncIssueTemplateTimer?.Dispose();
      _syncIssueTemplateTimer = null;

      await Save();
      await base.OnDeactivateAsync();
    }

    private async Task Save() {
      using (var context = _contextFactory.CreateInstance()) {
        await context.SaveRepositoryMetadata(
        _repoId,
        _repoSize,
        _metadata,
        _assignableMetadata,
        _issueMetadata,
        _issueSince,
        _labelMetadata,
        _milestoneMetadata,
        _projectMetadata,
        _contentsRootMetadata,
        _contentsDotGithubMetadata,
        _contentsIssueTemplateMetadata,
        _contentsPullRequestTemplateMetadata,
        _pullRequestMetadata,
        _pullRequestUpdatedAt,
        _pullRequestSkip,
        _protectedBranchMetadata);
      }
    }

    public async Task ForceSyncAllLinkedAccountRepositories() {
      IEnumerable<long> linkedAccountIds;
      using (var context = _contextFactory.CreateInstance()) {
        linkedAccountIds = await context.AccountRepositories
          .Where(x => x.RepositoryId == _repoId)
          .Where(x => x.Account.Tokens.Any())
          .Select(x => x.AccountId)
          .ToArrayAsync();
      }

      // Best Effort
      foreach (var userId in linkedAccountIds) {
        _grainFactory.GetGrain<IUserActor>(userId).SyncRepositories().LogFailure();
      }
    }

    public async Task ForceResyncRepositoryIssues() {
      _needsForceResyncIssues = true;
      await Sync();
    }

    private async Task ForceResyncRepositoryIssuesIfNeeded(DataUpdater updater) {
      if (!_needsForceResyncIssues) {
        return;
      }

      this.Info("Force resyncing issues");

      _needsForceResyncIssues = false;

      await updater.ForceResyncRepositoryIssues(_repoId);

      _issueMetadata = null;
      _pullRequestMetadata = null;
      _issueSince = EpochUtility.EpochOffset;
      _pullRequestSkip = 0;
      _pullRequestUpdatedAt = null;
      _issuesFullyImported = false;
      _syncCount = 0;
    }

    // ////////////////////////////////////////////////////////////
    // Utility Functions
    // ////////////////////////////////////////////////////////////

    private async Task<IEnumerable<(long UserId, bool IsAdmin)>> GetUsersWithAccess() {
      using (var context = _contextFactory.CreateInstance()) {
        // TODO: Keep this cached and current instead of looking it up every time.
        var users = new HashSet<(long UserId, bool IsAdmin)>(KeyEqualityComparer.FromKeySelector(((long UserId, bool IsAdmin) x) => x.UserId));

        // Always use members for sync
        var members = await context.AccountRepositories
            .AsNoTracking()
            .Where(x => x.RepositoryId == _repoId)
            .Where(x => x.Account.Tokens.Any())
            .Where(x => x.Account.RateLimit > GitHubRateLimit.RateLimitFloor || x.Account.RateLimitReset < DateTime.UtcNow)
            .Select(x => new { UserId = x.AccountId, Admin = x.Admin })
            .ToArrayAsync();

        if (members.Any()) {
          users.UnionWith(members.Select(x => (UserId: x.UserId, Admin: x.Admin)));
        }

        // If public, AND (there are no members OR the repo is NOT disabled) also use interested users
        if (!_isPrivate
          && (!members.Any() || !_disabled)) {
          var watchers = await context.AccountSyncRepositories
            .AsNoTracking()
            .Where(x => x.RepositoryId == _repoId)
            .Where(x => x.Account.Tokens.Any())
            .Where(x => x.Account.RateLimit > GitHubRateLimit.RateLimitFloor || x.Account.RateLimitReset < DateTime.UtcNow)
            .Select(x => x.AccountId)
            .ToArrayAsync();

          if (watchers.Any()) {
            users.UnionWith(watchers.Select(x => (UserId: x, Admin: false)));
          }
        }

        return users;
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
      if (_syncIssueTemplateTimer != null) {
        _syncIssueTemplateTimer.Dispose();
        _syncIssueTemplateTimer = null;
      }

      if (_idle && DateTimeOffset.UtcNow.Subtract(_lastSyncInterest) > SyncIdle) {
        DeactivateOnIdle();
        return;
      }

      var users = await GetUsersWithAccess();

      if (!users.Any()) {
        DeactivateOnIdle();
        return;
      }

      var github = new GitHubActorPool(_grainFactory, users.Select(x => x.UserId));

      IGitHubRepositoryAdmin admin = null;
      if (users.Any(x => x.IsAdmin)) {
        admin = _grainFactory.GetGrain<IGitHubActor>(users.First(x => x.IsAdmin).UserId);
      }

      var updater = new DataUpdater(_contextFactory, _mapper);
      try {
        await ForceResyncRepositoryIssuesIfNeeded(updater);
        await UpdateDetails(updater, github);

        // Private repos in orgs that have reverted to the free plan show in users'
        // repo lists but are inaccessible (404). We mark such repos _disabled until
        // we can access them.
        if (_disabled) {
          return; // Do nothing else until an update succeeds
        } else {
          await UpdateIssueTemplate(updater, github);
          await UpdateAssignees(updater, github);
          await UpdateLabels(updater, github);
          await UpdateMilestones(updater, github);

          if (_hasProjects) {
            await UpdateProjects(updater, github);
          }

          /* Nibblers
            * 
            * Things that nibble are tricky, as they have dependencies on one another.
            * We have to sync all issues before events, comments, and PRs.
            * This sucks but unless we drop referential integrity it's required.
            */
          await UpdateIssues(updater, github);

          /* IFF there are no issue changes, it's *probably* ok to sync the rest, in
            * priority order. It's still possible to race and discover (for example)
            * a PR before its Issue. For now let this fail - it'll eventually complete.
            */
          if (!updater.IssuesChanged) {
            await UpdatePullRequests(updater, github);
            await UpdateComments(updater, github);
          }
        }

        // Probably best to keep last
        if (admin != null) {
          updater.UnionWithExternalChanges(await AddOrUpdateWebhooks(admin));
        }
      } catch (GitHubPoolEmptyException) {
        // Nothing to do.
        // No need to also catch GithubRateLimitException, it's handled by GitHubActorPool
      }

      // Send Changes.
      await updater.Changes.Submit(_queueClient);
      _idle = updater.Changes.IsEmpty;

      // Save
      if (_syncCount % SaveSkip == 0) {
        await Save();
      }

      _syncCount++; // Last so it remains zero first run
    }

    private async Task UpdateDetails(DataUpdater updater, IGitHubPoolable github) {
      if (_metadata.IsExpired()) {
        var repo = await github.Repository(_fullName, _metadata);

        if (repo.Status == HttpStatusCode.NotFound) {
          // private repo in unpaid org?
          // public repo gone private?
          _disabled = true;
          // we're not even allowed to get the repo info, so I had to make a special method
          await updater.DisableRepository(_repoId);
        } else {
          // If we can read it, it's not disabled.
          // Even a cache match means it's valid
          _disabled = false;
        }

        if (repo.IsOk) {
          // It's possible the repo with this Id has been deleted, and a new one
          // with the same name has been created. If that has happened, ABORT ABORT ABORT
          if (repo.Result.Id != _repoId) {
            _disabled = true;
            DeactivateOnIdle();
            return;
          }

          await updater.UpdateRepositories(repo.Date, new[] { repo.Result }, enable: true);

          // Update cached local state
          _fullName = repo.Result.FullName;
          _repoSize = repo.Result.Size;
          _hasProjects = repo.Result.HasProjects;
        }

        // Don't update until saved.
        _metadata = GitHubMetadata.FromResponse(repo);
      }
    }

    public Task SyncIssueTemplate() {
      this.Trace();

      if (_syncIssueTemplateTimer != null) {
        _syncIssueTemplateTimer.Dispose();
      }
      _needsIssueTemplateSync = true;
      _syncIssueTemplateTimer = RegisterTimer(SyncIssueTemplateCallback, null, SyncIssueTemplateHysteresis, TimeSpan.MaxValue);
      return Task.CompletedTask;
    }

    private async Task SyncIssueTemplateCallback(object state) {
      this.Trace();

      _syncIssueTemplateTimer.Dispose();
      _syncIssueTemplateTimer = null;

      if (!_needsIssueTemplateSync) {
        return;
      }

      var users = await GetUsersWithAccess();
      if (!users.Any()) {
        DeactivateOnIdle(); // Sync may not even be running.
        return;
      }

      var github = new GitHubActorPool(_grainFactory, users.Select(x => x.UserId));

      var updater = new DataUpdater(_contextFactory, _mapper);
      await UpdateIssueTemplate(updater, github);
      await updater.Changes.Submit(_queueClient);
    }

    private static bool IsIssueTemplateFile(Common.GitHub.Models.ContentsFile file) {
      return file.Type == Common.GitHub.Models.ContentsFileType.File
            && file.Name != null
            && ExactMatchIssueTemplateRegex.IsMatch(file.Name);
    }

    private static bool IsPullRequestTemplateFile(Common.GitHub.Models.ContentsFile file) {
      return file.Type == Common.GitHub.Models.ContentsFileType.File
            && file.Name != null
            && ExactMatchPullRequestTemplateRegex.IsMatch(file.Name);
    }

    private async Task UpdateIssueTemplate(DataUpdater updater, IGitHubPoolable github) {
      if (!(_needsIssueTemplateSync || _pollIssueTemplate && (_syncCount % PollIssueTemplateSkip == 0))) {
        this.Info($"{_fullName} skipping ISSUE_TEMPLATE sync");
        return;
      }
      this.Info($"{_fullName} performing ISSUE_TEMPLATE sync");

      if (_repoSize == 0) {
        // realartists/shiphub-server#282 Handle 404 on repos/.../contents/
        // If there's never been a commit to the repo, the size will be 0, 
        // and the contents API will return 404s. Because 404s are not cacheable,
        // we want to be able to short circuit here and anticipate that and
        // just set an empty ISSUE_TEMPLATE in this case.
        await UpdateIssueTemplates(updater, null, null);
        _needsIssueTemplateSync = false;
        return;
      }

      var prevRootMetadata = _contentsRootMetadata;
      var prevDotGitHubMetadata = _contentsDotGithubMetadata;
      var prevIssueTemplateMetadata = _contentsIssueTemplateMetadata;
      var prevPullRequestTemplateMetadata = _contentsPullRequestTemplateMetadata;
      try {
        await _UpdateIssueTemplate(updater, github);
        _needsIssueTemplateSync = false;
      } catch (Exception ex) {
        // unwind anything we've discovered about the cache state if we couldn't finish the whole operation
        _contentsRootMetadata = prevRootMetadata;
        _contentsDotGithubMetadata = prevDotGitHubMetadata;
        _contentsIssueTemplateMetadata = prevIssueTemplateMetadata;
        _contentsPullRequestTemplateMetadata = prevPullRequestTemplateMetadata;
        this.Exception(ex);
        throw;
      }
    }

    private async Task _UpdateIssueTemplate(DataUpdater updater, IGitHubPoolable github) {
      if (_contentsIssueTemplateMetadata == null || _contentsPullRequestTemplateMetadata == null) {
        // then we don't have either issue template or pull request template, and we have to search for them
        // even if we have just one, we still have to search, since the second could appear at any time.
        var rootListing = await github.ListDirectoryContents(_fullName, "/", _contentsRootMetadata);
        _contentsRootMetadata = rootListing.Succeeded ? GitHubMetadata.FromResponse(rootListing) : null;

        if (rootListing.IsOk) {
          // search the root listing for any matching ISSUE_TEMPLATE files
          var rootIssueTemplateFile = rootListing.Result.FirstOrDefault(IsIssueTemplateFile);
          var rootPullRequestTemplateFile = rootListing.Result.FirstOrDefault(IsPullRequestTemplateFile);
          GitHubResponse<byte[]> issueTemplateContent = null;
          GitHubResponse<byte[]> pullRequestTemplateContent = null;

          if (rootIssueTemplateFile == null && rootPullRequestTemplateFile == null) {
            var hasDotGitHub = rootListing.Result.Any((file) => {
              return file.Type == Common.GitHub.Models.ContentsFileType.Dir
                && file.Name != null
                && file.Name.ToLowerInvariant() == ".github";
            });

            // NB: If /.github's contents change, then the ETag on / also changes
            // Which means that we're fine to only search /.github in the event that
            // / returns 200 (and not 304).
            if (hasDotGitHub) {
              var dotGitHubListing = await github.ListDirectoryContents(_fullName, "/.github", _contentsDotGithubMetadata);
              _contentsDotGithubMetadata = dotGitHubListing.Succeeded ? GitHubMetadata.FromResponse(dotGitHubListing) : null;

              if (dotGitHubListing.IsOk) {
                var dotGitHubIssueTemplateFile = dotGitHubListing.Result.FirstOrDefault(IsIssueTemplateFile);
                var dotGitHubPullRequestTemplateFile = dotGitHubListing.Result.FirstOrDefault(IsPullRequestTemplateFile);
                if (dotGitHubIssueTemplateFile != null) {
                  issueTemplateContent = await github.FileContents(_fullName, dotGitHubIssueTemplateFile.Path);
                }
                if (dotGitHubPullRequestTemplateFile != null) {
                  pullRequestTemplateContent = await github.FileContents(_fullName, dotGitHubPullRequestTemplateFile.Path);
                }
              }
            }
          } else /* either or both root*TemplateFile != null */ {
            if (rootIssueTemplateFile != null) {
              issueTemplateContent = await github.FileContents(_fullName, rootIssueTemplateFile.Path);
            }
            if (rootPullRequestTemplateFile != null) {
              pullRequestTemplateContent = await github.FileContents(_fullName, rootPullRequestTemplateFile.Path);
            }
          }

          _contentsIssueTemplateMetadata = issueTemplateContent?.Succeeded == true ? GitHubMetadata.FromResponse(issueTemplateContent) : null;
          _contentsPullRequestTemplateMetadata = pullRequestTemplateContent?.Succeeded == true ? GitHubMetadata.FromResponse(pullRequestTemplateContent) : null;
          if ((issueTemplateContent != null && issueTemplateContent.IsOk) || (pullRequestTemplateContent != null && pullRequestTemplateContent.IsOk)) {
            await UpdateIssueTemplates(updater,
              DecodeIssueTemplate(issueTemplateContent?.Result),
              DecodeIssueTemplate(pullRequestTemplateContent?.Result));
            return;
          }
        }
        return; // couldn't find any ISSUE_TEMPLATE/PULL_REQUEST_TEMPLATE anywhere
      } else {
        // we have some cached data on an existing ISSUE_TEMPLATE and PULL_REQUEST_TEMPLATE.
        // try to short-circuit by just looking them up.
        // If we get a 404, then we start over from the top
        var prefix = $"repos/{_fullName}/contents";

        var paths = new[] { _contentsIssueTemplateMetadata, _contentsPullRequestTemplateMetadata }
          .Select(x => x.Path.Substring(prefix.Length));

        var contents = await Task.WhenAll(paths.Select(x => github.FileContents(_fullName, x)));

        _contentsIssueTemplateMetadata = contents[0].Succeeded ? GitHubMetadata.FromResponse(contents[0]) : null;
        _contentsPullRequestTemplateMetadata = contents[1].Succeeded ? GitHubMetadata.FromResponse(contents[1]) : null;

        string issueTemplateContent, pullRequestTemplateContent;

        switch (contents[0].Status) {
          case HttpStatusCode.OK:
            issueTemplateContent = DecodeIssueTemplate(contents[0].Result);
            break;
          case HttpStatusCode.NotModified:
            issueTemplateContent = _issueTemplateContent;
            break;
          default:
            issueTemplateContent = null;
            break;
        }

        switch (contents[1].Status) {
          case HttpStatusCode.OK:
            pullRequestTemplateContent = DecodeIssueTemplate(contents[1].Result);
            break;
          case HttpStatusCode.NotModified:
            pullRequestTemplateContent = _pullRequestTemplateContent;
            break;
          default:
            pullRequestTemplateContent = null;
            break;
        }

        if (contents.Any(x => x.Status == HttpStatusCode.NotFound)) {
          // one or both have been deleted or moved. update them optimistically, but then start a new search from the top
          await UpdateIssueTemplates(updater, issueTemplateContent, pullRequestTemplateContent);
        } else if (contents.Any(x => x.Status == HttpStatusCode.OK)) {
          await UpdateIssueTemplates(updater, issueTemplateContent, pullRequestTemplateContent);
        } else {
          // nothing changed as far as we can tell
        }
      }
    }

    private static string DecodeIssueTemplate(byte[] data) {
      if (data != null) {
        return Encoding.UTF8.GetString(data);
      }
      return null;
    }

    private async Task UpdateIssueTemplates(DataUpdater updater, string issueTemplate, string pullRequestTemplate) {
      if (_issueTemplateContent != issueTemplate || _pullRequestTemplateContent != pullRequestTemplate) {
        await updater.SetRepositoryIssueTemplate(_repoId, issueTemplate, pullRequestTemplate);
        _issueTemplateContent = issueTemplate;
        _pullRequestTemplateContent = pullRequestTemplate;
      }
    }

    private async Task UpdateAssignees(DataUpdater updater, IGitHubPoolable github) {
      if (_assignableMetadata.IsExpired()) {
        var assignees = await github.Assignable(_fullName, _assignableMetadata);
        if (assignees.IsOk) {
          await updater.SetRepositoryAssignees(_repoId, assignees.Date, assignees.Result);
        }
        _assignableMetadata = GitHubMetadata.FromResponse(assignees);
      }
    }

    private async Task UpdateLabels(DataUpdater updater, IGitHubPoolable github) {
      if (_labelMetadata.IsExpired()) {
        var labels = await github.Labels(_fullName, _labelMetadata);
        if (labels.IsOk) {
          await updater.UpdateLabels(_repoId, labels.Result, complete: true);
        }
        _labelMetadata = GitHubMetadata.FromResponse(labels);
      }
    }

    private async Task UpdateMilestones(DataUpdater updater, IGitHubPoolable github) {
      if (_milestoneMetadata.IsExpired()) {
        var milestones = await github.Milestones(_fullName, _milestoneMetadata);
        if (milestones.IsOk) {
          await updater.UpdateMilestones(_repoId, milestones.Date, milestones.Result, complete: true);
        }
        _milestoneMetadata = GitHubMetadata.FromResponse(milestones);
      }
    }

    private async Task UpdateProjects(DataUpdater updater, IGitHubPoolable github) {
      if (_projectMetadata.IsExpired()) {
        var projects = await github.RepositoryProjects(_fullName, _projectMetadata);
        if (projects.IsOk) {
          await updater.UpdateRepositoryProjects(_repoId, projects.Date, projects.Result);
        } else if (projects.Status == HttpStatusCode.Gone || projects.Status == HttpStatusCode.NotFound) {
          // Not enough to rely on UpdateDetails alone.
          _hasProjects = false;
        }
        _projectMetadata = GitHubMetadata.FromResponse(projects);
      }
    }

    private async Task UpdateIssues(DataUpdater updater, IGitHubPoolable github) {
      if (_issueMetadata.IsExpired()) {
        var issueResponse = await github.Issues(_fullName, _issueSince, BiteChunkPages, _issueMetadata);
        if (issueResponse.IsOk) {
          var nibbledIssues = issueResponse.Result;
          var issues = nibbledIssues;

          if (_issueSince == EpochUtility.EpochOffset && issueResponse.CacheData == null) {
            this.Info($"{_fullName} will load newest issues to tide itself over");
            // in this scenario we're just starting nibbling, but we couldn't get it all in
            // one go. do a single additional request to get the very newest issues by
            // creation date. this is done both so we have recent data to show initially,
            // and to help estimate spider progress.
            var newestIssuesResponse = await github.NewestIssues(_fullName);
            if (newestIssuesResponse.IsOk) {
              issues = newestIssuesResponse.Result.Concat(nibbledIssues).Distinct(x => x.Id);
            } else {
              this.Error($"{_fullName} failed to load newest issues: {newestIssuesResponse.Status}");
            }
          }
          await updater.UpdateIssues(_repoId, issueResponse.Date, issues);

          if (updater.IssuesChanged) {
            // Ensure we don't miss any when we hit the page limit.
            _issueSince = nibbledIssues.Max(x => x.UpdatedAt).AddSeconds(-5);
            await updater.UpdateRepositoryIssueSince(_repoId, _issueSince);
          }
        }

        /*
         * Sync is "complete" if
         * 1. We see a NotModified.
         *   - This covers repos synced before completion tracking.
         * 2. We see a full response (not max-page limited).
         *   - This is indicated by HTTP OK + non-null cache metadata.
         *   - Partial responses don't include cache metadata.
         * Mark sync complete ASAP since we want to update clients as soon as possible.
         */
        if (((issueResponse.IsOk && issueResponse.CacheData != null) || (issueResponse.Status == HttpStatusCode.NotModified))
            && !_issuesFullyImported) {
          this.Info($"{_fullName} Issues are now fully imported");
          await updater.MarkRepositoryIssuesAsFullyImported(_repoId);
          _issuesFullyImported = true;
        }

        // This is safe, even when still nibbling because the since parameter will
        // continue incrementing and invalidating the ETag.
        _issueMetadata = GitHubMetadata.FromResponse(issueResponse);
      }
    }

    private async Task UpdatePullRequests(DataUpdater updater, IGitHubPoolable github) {
      /* Because GitHub is evil, we can't sync PRs like we do Issues.
       * 
       * Per https://developer.github.com/v3/pulls/#list-pull-requests there is no
       * "since" parameter. This forces us to sync PRs in two stages. First, sync
       * from the bottom up in "created asc" order, skipping more pages each nibble.
       * Then, once we have all the PRs, sync using "updated desc" with a reasonable
       * page limit. Keep walking until we see a record we've seen before.
       * 
       * Alternately, we can just grab some fixed number of pages and rely on
       * on-demand refresh and hooks to fill in anything we miss (rare).
       */

      if (_pullRequestUpdatedAt == null) {
        // Inital sync. Walk from bottom up.
        var prCreated = await github.PullRequests(_fullName, "created", "asc", _pullRequestSkip, BiteChunkPages);
        if (prCreated.IsOk) {
          var hasResults = prCreated.Result.Any();
          if (hasResults) {
            await updater.UpdatePullRequests(_repoId, prCreated.Date, prCreated.Result);

            // Yes, this can miss newly created issues on the last page.
            // The updated_at base sync that takes over will catch any omissions.
            _pullRequestSkip += prCreated.Pages;
          }

          if (!hasResults || prCreated.Pagination?.Next == null) {
            // This is gross. Is there a more correct solution?
            _pullRequestUpdatedAt = prCreated.Date.AddDays(-1);
          }
        }
      }

      if (_pullRequestUpdatedAt != null) {
        uint skip = 0;
        GitHubResponse<IEnumerable<Common.GitHub.Models.PullRequest>> updated = null;
        GitHubMetadata firstPageMetadata = null;
        var mostRecent = _pullRequestUpdatedAt ?? DateTimeOffset.MinValue;

        do {
          // Walk from most recently updated down.
          // If not skipping any pages, send cache metadata to (if no changes) get a 304 No Modified
          updated = await github.PullRequests(_fullName, "updated", "desc", skip, PullRequestUpdateChunkSize, skip == 0 ? _pullRequestMetadata : null);

          // Grab it from the first page in case the data changes under us during pagination.
          // Don't want to miss anything permanently.
          if (firstPageMetadata == null) {
            firstPageMetadata = GitHubMetadata.FromResponse(updated);
          }

          if (updated.IsOk) {
            if (updated.Result.Any()) {
              await updater.UpdatePullRequests(_repoId, updated.Date, updated.Result);
              skip += updated.Pages;

              var batchMostRecent = updated.Result
                .Select(x => x.UpdatedAt)
                .OrderByDescending(x => x)
                .FirstOrDefault();

              // Track the most recent update we've seen.
              if (mostRecent < batchMostRecent) {
                mostRecent = batchMostRecent;
              }

              if (_pullRequestUpdatedAt != null && batchMostRecent < _pullRequestUpdatedAt.Value) {
                // The most recent update in this batch is older than the data we already have - Bail.
                break;
              }
            } else {
              // All results enumerated - Bail.
              break;
            }
          }
        } while (updated.IsOk && updated.Pagination?.Next != null);

        _pullRequestMetadata = firstPageMetadata;
        _pullRequestUpdatedAt = mostRecent;
      }
    }

    private async Task UpdateComments(DataUpdater updater, IGitHubPoolable github) {
      if (_commentMetadata.IsExpired()) {
        var response = await github.Comments(_fullName, _commentSince, BiteChunkPages, _commentMetadata);

        try {
          if (response.IsOk && response.Result.Any()) {
            await updater.UpdateIssueComments(_repoId, response.Date, response.Result);

            // Ensure we don't miss any when we hit the page limit.
            var newSince = response.Result.Max(x => x.UpdatedAt).AddSeconds(-5);
            if (newSince != _commentSince) {
              await updater.UpdateRepositoryCommentSince(_repoId, newSince);
              _commentSince = newSince;
            }
          }
        } catch (Exception e) {
          Log.Info($"{_fullName}[{_commentSince:o}]: {e}");
          throw;
        }

        // This is safe, even when still nibbling because the since parameter will
        // continue incrementing and invalidating the ETag.
        _commentMetadata = GitHubMetadata.FromResponse(response);
      }
    }

    public async Task<IChangeSummary> AddOrUpdateWebhooks(IGitHubRepositoryAdmin admin) {
      var changes = ChangeSummary.Empty;

      Hook hook = null;
      HookTableType newHook = null;
      using (var context = _contextFactory.CreateInstance()) {
        hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.RepositoryId == _repoId);
        // If our last operation on this repo hook resulted in error, delay.
        if (hook?.LastError != null && hook?.LastError.Value > DateTimeOffset.UtcNow.Subtract(HookErrorDelay)) {
          return changes; // Wait to try later.
        }
        if (hook?.GitHubId == null) {
          // GitHub will immediately send a ping when the webhook is created.
          // To avoid any chance for a race, add the Hook to the DB first, then
          // create on GitHub.
          if (hook == null) {
            newHook = await context.CreateHook(Guid.NewGuid(), string.Join(",", RequiredEvents), repositoryId: _repoId);
          } else {
            newHook = new HookTableType() {
              Id = hook.Id,
              Secret = hook.Secret,
              Events = string.Join(",", RequiredEvents),
            };
          }

          // Assume failure until we succeed
          newHook.LastError = DateTimeOffset.UtcNow;
        }
      }

      // There are now a few cases to handle
      // If there is no record of a hook, try to make one.
      // If there is an incomplete record, try to make it.
      // If there is an errored record, sleep or retry
      if (hook?.GitHubId == null) {
        try {
          var hookList = await admin.RepositoryWebhooks(_fullName);
          if (!hookList.IsOk) {
            this.Info($"Unable to list hooks for {_fullName}. {hookList.Status} {hookList.Error}");
            return changes;
          }

          var existingHooks = hookList.Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{_apiHostName}/", StringComparison.OrdinalIgnoreCase));

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in existingHooks) {
            var deleteResponse = await admin.DeleteRepositoryWebhook(_fullName, existingHook.Id);
            if (!deleteResponse.Succeeded) {
              this.Info($"Failed to delete existing hook ({existingHook.Id}) for repo '{_fullName}'{deleteResponse.Status} {deleteResponse.Error}");
            }
          }

          var addRepoHookResponse = await admin.AddRepositoryWebhook(
            _fullName,
            new Common.GitHub.Models.Webhook() {
              Name = "web",
              Active = true,
              Events = RequiredEvents,
              Config = new Common.GitHub.Models.WebhookConfiguration() {
                Url = $"https://{_apiHostName}/webhook/repo/{_repoId}",
                ContentType = "json",
                Secret = newHook.Secret.ToString(),
              },
            });

          if (addRepoHookResponse.Succeeded) {
            newHook.GitHubId = addRepoHookResponse.Result.Id;
            newHook.LastError = null;
            using (var context = _contextFactory.CreateInstance()) {
              changes = await context.BulkUpdateHooks(hooks: new[] { newHook });
            }
          } else {
            this.Error($"Failed to add hook for repo '{_fullName}' ({_repoId}): {addRepoHookResponse.Status} {addRepoHookResponse.Error}");
          }
        } catch (Exception e) {
          e.Report($"Failed to add hook for repo '{_fullName}' ({_repoId})");
          // Save LastError
          using (var context = _contextFactory.CreateInstance()) {
            await context.BulkUpdateHooks(hooks: new[] { newHook });
          }
        }
      } else if (!RequiredEvents.SetEquals(hook.Events.Split(','))) {
        var editHook = new HookTableType() {
          Id = hook.Id,
          GitHubId = hook.GitHubId,
          Secret = hook.Secret,
          Events = hook.Events,
          LastError = DateTimeOffset.UtcNow, // Default to faulted.
        };

        try {
          this.Info($"Updating webhook {_fullName}/{hook.GitHubId} from [{hook.Events}] to [{string.Join(",", RequiredEvents)}]");
          var editResponse = await admin.EditRepositoryWebhookEvents(_fullName, (long)hook.GitHubId, RequiredEvents);

          if (editResponse.Succeeded) {
            editHook.LastError = null;
            editHook.GitHubId = editResponse.Result.Id;
            editHook.Events = string.Join(",", editResponse.Result.Events);
            using (var context = _contextFactory.CreateInstance()) {
              await context.BulkUpdateHooks(hooks: new[] { editHook });
            }
          } else if (editResponse.Status == HttpStatusCode.NotFound) {
            // Our record is out of date.
            this.Info($"Failed to edit hook for repo '{_fullName}' ({_repoId}). Deleting our hook record. {editResponse.Status} {editResponse.Error}");
            using (var context = _contextFactory.CreateInstance()) {
              changes = await context.BulkUpdateHooks(deleted: new[] { editHook.Id });
            }
          } else {
            throw new Exception($"Failed to edit hook for repo '{_fullName}' ({_repoId}): {editResponse.Status} {editResponse.Error}");
          }
        } catch (Exception e) {
          e.Report();
          // Save LastError
          using (var context = _contextFactory.CreateInstance()) {
            await context.BulkUpdateHooks(hooks: new[] { editHook });
          }
        }
      }

      return changes;
    }

    public async Task SyncProtectedBranch(string branchName, long forUserId) {
      var changes = new ChangeSummary();
      IGitHubActor ghc;
      var metadata = _protectedBranchMetadata.Val(branchName);
      if (metadata != null) {
        // to work around a GitHub bug, prefer to re-request with the user who succeeded last time.
        ghc = _grainFactory.GetGrain<IGitHubActor>(metadata.UserId);
      } else {
        ghc = _grainFactory.GetGrain<IGitHubActor>(forUserId);
      }

      var branchProtectionResponse = await ghc.BranchProtection(_fullName, branchName, metadata, RequestPriority.Interactive);

      metadata = GitHubMetadata.FromResponse(branchProtectionResponse);

      if (branchProtectionResponse.IsOk) {
        using (var context = _contextFactory.CreateInstance()) {
          changes.UnionWith(await context.UpdateProtectedBranch(_repoId, branchName, branchProtectionResponse.Result.SerializeObject(), metadata));
        }
      }

      _protectedBranchMetadata[branchName] = metadata;

      if (!changes.IsEmpty) {
        await _queueClient.NotifyChanges(changes);
      }
    }

    public async Task RefreshIssueComment(long commentId) {
      var users = await GetUsersWithAccess();

      if (!users.Any()) {
        return;
      }

      var github = new GitHubActorPool(_grainFactory, users.Select(x => x.UserId));

      var updater = new DataUpdater(_contextFactory, _mapper);
      try {
        // TODO: Lookup Metadata?
        var commentResponse = await github.IssueComment(_fullName, commentId, null, RequestPriority.Background);
        if (commentResponse.IsOk) {
          await updater.UpdateIssueComments(_repoId, commentResponse.Date, new[] { commentResponse.Result });
        }
        // TODO: Reactions?
      } catch (GitHubPoolEmptyException) {
        // Nothing to do.
        // No need to also catch GithubRateLimitException, it's handled by GitHubActorPool
      }

      // Send Changes.
      await updater.Changes.Submit(_queueClient);
    }

    public async Task RefreshPullRequestReviewComment(long commentId) {
      var users = await GetUsersWithAccess();

      if (!users.Any()) {
        return;
      }

      var github = new GitHubActorPool(_grainFactory, users.Select(x => x.UserId));

      var updater = new DataUpdater(_contextFactory, _mapper);
      try {
        // TODO: Lookup Metadata?
        long? issueId;
        using (var context = _contextFactory.CreateInstance()) {
          issueId = await context.PullRequestComments
            .AsNoTracking()
            .Where(x => x.RepositoryId == _repoId && x.Id == commentId)
            .Select(x => (long?)x.IssueId)
            .SingleOrDefaultAsync();
        }

        if (issueId.HasValue) {
          // We can't update comments until we know about the issue, sadly.
          // Luckily, this method is a hack that's only used for edited comments, which we're more likely to already have.
          var commentResponse = await github.PullRequestComment(_fullName, commentId, null, RequestPriority.Background);
          if (commentResponse.IsOk) {
            await updater.UpdatePullRequestComments(_repoId, issueId.Value, commentResponse.Date, new[] { commentResponse.Result });
          }
        }
        // TODO: Reactions?
      } catch (GitHubPoolEmptyException) {
        // Nothing to do.
        // No need to also catch GithubRateLimitException, it's handled by GitHubActorPool
      }

      // Send Changes.
      await updater.Changes.Submit(_queueClient);
    }
  }
}
