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
  using Common.Hashing;
  using GitHub;
  using Orleans;
  using QueueClient;

  public class RepositoryActor : Grain, IRepositoryActor {
    private const int ChunkMaxPages = 75;

    public static readonly TimeSpan SyncDelay = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan HookErrorDelay = TimeSpan.FromHours(12);
    public static readonly TimeSpan SyncIdle = TimeSpan.FromSeconds(SyncDelay.TotalSeconds * 3);
    public static readonly TimeSpan SyncIssueTemplateHysteresis = TimeSpan.FromSeconds(2);
    public static readonly int PollIssueTemplateSkip = 5; // If we have to poll the ISSUE_TEMPLATE, do it every N Syncs

    public static ImmutableHashSet<string> RequiredEvents { get; } = ImmutableHashSet.Create(
      "issues"
      , "issue_comment"
      , "label"
      , "milestone"
      , "push"
    );

    public static Regex ExactMatchIssueTemplateRegex { get; } = new Regex(
      @"^issue_template(?:\.\w+)?$",
      RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
      TimeSpan.FromMilliseconds(200));

    public static Regex EndsWithIssueTemplateRegex { get; } = new Regex(
      @"issue_template(?:\.\w+)?$",
      RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
      TimeSpan.FromMilliseconds(200));

    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    private long _repoId;
    private string _fullName;
    private long _repoSize;
    private string _issueTemplateHash;
    private bool _disabled;
    private string _apiHostName;
    private bool _projectsEnabled;

    // Metadata
    private GitHubMetadata _metadata;
    private GitHubMetadata _assignableMetadata;
    private GitHubMetadata _issueMetadata;
    private GitHubMetadata _labelMetadata;
    private GitHubMetadata _milestoneMetadata;
    private GitHubMetadata _projectMetadata;
    private GitHubMetadata _contentsRootMetadata;
    private GitHubMetadata _contentsDotGithubMetadata;
    private GitHubMetadata _contentsIssueTemplateMetadata;

    // Issue chunk tracking
    private DateTimeOffset _issueSince;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;
    private IDisposable _syncIssueTemplateTimer;
    private bool _needsIssueTemplateSync;
    private bool _pollIssueTemplate;
    private int _syncCount;

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

        Initialize(repo.Id, repo.FullName);
        _repoSize = repo.Size;
        _issueTemplateHash = HashIssueTemplate(repo.IssueTemplate);
        _metadata = repo.Metadata;
        _disabled = repo.Disabled;
        _assignableMetadata = repo.AssignableMetadata;
        _issueMetadata = repo.IssueMetadata;
        _issueSince = repo.IssueSince ?? EpochUtility.EpochOffset; // Reasonable default.
        _labelMetadata = repo.LabelMetadata;
        _milestoneMetadata = repo.MilestoneMetadata;
        _projectMetadata = repo.ProjectMetadata;
        _contentsRootMetadata = repo.ContentsRootMetadata;
        _contentsDotGithubMetadata = repo.ContentsDotGitHubMetadata;
        _contentsIssueTemplateMetadata = repo.ContentsIssueTemplateMetadata;

        // HACK: Read this from Repository response and persist.
        _projectsEnabled = true;

        // if we have no webhook, we must poll the ISSUE_TEMPLATE
        _pollIssueTemplate = await context.Hooks.Where(hook => hook.RepositoryId == _repoId && hook.LastSeen != null).AnyAsync();
        _needsIssueTemplateSync = _contentsRootMetadata == null;
        this.Info($"{_fullName} polls ISSUE_TEMPLATE:{_pollIssueTemplate}");
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
          _contentsIssueTemplateMetadata);
      }
    }

    // ////////////////////////////////////////////////////////////
    // Utility Functions
    // ////////////////////////////////////////////////////////////

    private async Task<IEnumerable<Tuple<long, bool>>> GetUsersWithAccess() {
      using (var context = _contextFactory.CreateInstance()) {
        // TODO: Keep this cached and current instead of looking it up every time.
        var users = await context.AccountRepositories
          .AsNoTracking()
          .Where(x => x.RepositoryId == _repoId)
          .Where(x => x.Account.Tokens.Any())
          .Where(x => x.Account.RateLimit > GitHubRateLimit.RateLimitFloor || x.Account.RateLimitReset < DateTime.UtcNow)
          .Select(x => new { UserId = x.AccountId, Admin = x.Admin })
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
      if (_syncIssueTemplateTimer != null) {
        _syncIssueTemplateTimer.Dispose();
        _syncIssueTemplateTimer = null;
      }

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

      IGitHubRepositoryAdmin admin = null;
      var firstAdmin = users.FirstOrDefault(x => x.Item2);
      if (firstAdmin != null) {
        admin = _grainFactory.GetGrain<IGitHubActor>(firstAdmin.Item1);
      }

      var changes = new ChangeSummary();
      try {
        _syncCount++;
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

    private async Task SyncTask(IGitHubPoolable github, IGitHubRepositoryAdmin admin, ChangeSummary changes) {
      using (var context = _contextFactory.CreateInstance()) {
        // Private repos in orgs that have reverted to the free plan show in users'
        // repo lists but are inaccessible (404). We mark such repos _disabled until
        // we can access them.
        changes.UnionWith(await UpdateDetails(context, github));

        if (_disabled) {
          DeactivateOnIdle();
        } else {
          changes.UnionWith(await UpdateIssueTemplate(context, github));
          changes.UnionWith(await UpdateAssignees(context, github));
          changes.UnionWith(await UpdateLabels(context, github));
          changes.UnionWith(await UpdateMilestones(context, github));
          
		  if (_projectsEnabled) {
            changes.UnionWith(await UpdateProjects(context, github));
          }

          var issueChanges = await UpdateIssues(context, github);
          changes.UnionWith(issueChanges);

          // Probably best to keep last
          if (admin != null) {
            changes.UnionWith(await AddOrUpdateWebhooks(context, admin));
          }
        }

        /* Comments
         * 
         * For now primary population is on demand.
         * Deletion is detected when checking for reactions.
         * Each sync cycle, check a few pages before and after current watermarks.
         * Note well: The watermarks MUST ONLY be updated from here. On demand
         * sync will sparsely populate the comment corpus, so there's not a way
         * to derive the overall sync status from the comments we've already synced.
         */

        /* Issue Events
         * 
         * For now primary population is on demand.
         * Each sync cycle, check a few pages before and after current watermarks.
         * Note well: The watermarks MUST ONLY be updated from here. On demand
         * sync will sparsely populate the event corpus, so there's not a way
         * to derive the overall sync status from the comments we've already synced.
         */
      }
    }

    private async Task<IChangeSummary> UpdateDetails(ShipHubContext context, IGitHubPoolable github) {
      var changes = ChangeSummary.Empty;

      if (_metadata.IsExpired()) {
        var repo = await github.Repository(_fullName, _metadata);

        if (repo.IsOk) {
          var repoTableType = _mapper.Map<RepositoryTableType>(repo.Result);

          // If we can read it, it's not disabled.
          _disabled = false;
          repoTableType.Disabled = false;

          changes = await context.BulkUpdateRepositories(repo.Date, new[] { repoTableType });
          _fullName = repo.Result.FullName;
          _repoSize = repo.Result.Size;
        } else if (repo.Status == HttpStatusCode.NotFound) {
          // private repo in unpaid org?
          _disabled = true;
          // we're not even allowed to get the repo info, so i had to make a special method
          changes = await context.DisableRepository(_repoId, _disabled);
        }

        // Don't update until saved.
        _metadata = GitHubMetadata.FromResponse(repo);
      }

      return changes;
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

      var github = new GitHubActorPool(_grainFactory, users.Select(x => x.Item1));

      ChangeSummary changes;
      using (var context = _contextFactory.CreateInstance()) {
        changes = await UpdateIssueTemplate(context, github);
      }
      if (!changes.IsEmpty) {
        await _queueClient.NotifyChanges(changes);
      }
    }

    private static bool IsTemplateFile(Common.GitHub.Models.ContentsFile file) {
      return file.Type == Common.GitHub.Models.ContentsFileType.File
            && file.Name != null
            && ExactMatchIssueTemplateRegex.IsMatch(file.Name);
    }

    private async Task<ChangeSummary> UpdateIssueTemplate(ShipHubContext context, IGitHubPoolable github) {
      if (!(_needsIssueTemplateSync || _pollIssueTemplate && (_syncCount - 1 % PollIssueTemplateSkip == 0))) {
        this.Info($"{_fullName} skipping ISSUE_TEMPLATE sync");
        return new ChangeSummary();
      }
      this.Info($"{_fullName} performing ISSUE_TEMPLATE sync");

      if (_repoSize == 0) {
        // realartists/shiphub-server#282 Handle 404 on repos/.../contents/
        // If there's never been a commit to the repo, the size will be 0, 
        // and the contents API will return 404s. Because 404s are not cacheable,
        // we want to be able to short circuit here and anticipate that and
        // just set an empty ISSUE_TEMPLATE in this case.
        var ret = await UpdateIssueTemplateWithResult(context, null);
        _needsIssueTemplateSync = false;
        return ret;
      }

      var prevRootMetadata = _contentsRootMetadata;
      var prevDotGitHubMetadata = _contentsDotGithubMetadata;
      var prevIssueTemplateMetadata = _contentsIssueTemplateMetadata;
      try {
        var ret = await _UpdateIssueTemplate(context, github);
        _needsIssueTemplateSync = false;
        return ret;
      } catch (Exception ex) {
        // unwind anything we've discovered about the cache state if we couldn't finish the whole operation
        _contentsRootMetadata = prevRootMetadata;
        _contentsDotGithubMetadata = prevDotGitHubMetadata;
        _contentsIssueTemplateMetadata = prevIssueTemplateMetadata;
        this.Exception(ex);
        throw;
      }
    }

    private async Task<ChangeSummary> _UpdateIssueTemplate(ShipHubContext context, IGitHubPoolable github) {
      if (_contentsIssueTemplateMetadata == null) {
        // then we don't have any known IssueTemplate, we have to search for it
        var rootListing = await github.ListDirectoryContents(_fullName, "/", _contentsRootMetadata);
        _contentsRootMetadata = rootListing.Succeeded ? GitHubMetadata.FromResponse(rootListing) : null;

        if (rootListing.IsOk) {
          // search the root listing for any matching ISSUE_TEMPLATE files
          var rootTemplateFile = rootListing.Result.FirstOrDefault(IsTemplateFile);
          GitHubResponse<byte[]> templateContent = null;

          if (rootTemplateFile == null) {
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
                var dotGitHubTemplateFile = dotGitHubListing.Result.FirstOrDefault(IsTemplateFile);
                if (dotGitHubTemplateFile != null) {
                  templateContent = await github.FileContents(_fullName, dotGitHubTemplateFile.Path);
                }
              }
            }
          } else /* rootTemplateFile != null */ {
            templateContent = await github.FileContents(_fullName, rootTemplateFile.Path);
          }

          _contentsIssueTemplateMetadata = templateContent?.Succeeded == true ? GitHubMetadata.FromResponse(templateContent) : null;
          if (templateContent != null && templateContent.IsOk) {
            return await UpdateIssueTemplateWithResult(context, templateContent.Result);
          }
        }
        return new ChangeSummary(); // couldn't find any ISSUE_TEMPLATE anywhere
      } else {
        // we have some cached data on an existing ISSUE_TEMPLATE.
        // try to short-circuit by just looking it up.
        // If we get a 404, then we start over from the top
        var filePath = _contentsIssueTemplateMetadata.Path.Substring($"repos/{_fullName}/contents".Length);
        var templateContent = await github.FileContents(_fullName, filePath);
        _contentsIssueTemplateMetadata = templateContent.Succeeded ? GitHubMetadata.FromResponse(templateContent) : null;

        if (templateContent.Status == HttpStatusCode.NotFound) {
          // it's been deleted or moved. clear it optimistically, but then start a new search from the top.
          var changes = new ChangeSummary();
          changes.UnionWith(await UpdateIssueTemplateWithResult(context, null));
          changes.UnionWith(await _UpdateIssueTemplate(context, github));
          return changes;
        } else if (templateContent.IsOk) {
          return await UpdateIssueTemplateWithResult(context, templateContent.Result);
        } else {
          return new ChangeSummary(); // nothing changed as far as we can tell
        }
      }
    }

    private static string HashIssueTemplate(string issueTemplate) {
      if (string.IsNullOrEmpty(issueTemplate)) {
        return "";
      }

      string hashString;
      using (var hashFunction = new MurmurHash3()) {
        var hash = hashFunction.ComputeHash(Encoding.UTF8.GetBytes(issueTemplate));
        hashString = new Guid(hash).ToString();
      }

      return hashString;
    }

    private async Task<ChangeSummary> UpdateIssueTemplateWithResult(ShipHubContext context, byte[] templateData) {
      string templateStr = null;
      if (templateData != null) {
        templateStr = Encoding.UTF8.GetString(templateData);
      }

      var newHash = HashIssueTemplate(templateStr);
      if (_issueTemplateHash != newHash) {
        var ret = await context.SetRepositoryIssueTemplate(_repoId, templateStr);
        _issueTemplateHash = newHash;
        return ret;
      } else {
        return new ChangeSummary();
      }
    }

    private async Task<IChangeSummary> UpdateAssignees(ShipHubContext context, IGitHubPoolable github) {
      var changes = new ChangeSummary();

      // Update Assignees
      if (_assignableMetadata.IsExpired()) {
        var assignees = await github.Assignable(_fullName, _assignableMetadata);
        if (assignees.IsOk) {
          changes.UnionWith(await context.BulkUpdateAccounts(assignees.Date, _mapper.Map<IEnumerable<AccountTableType>>(assignees.Result)));
          changes.UnionWith(await context.SetRepositoryAssignableAccounts(_repoId, assignees.Result.Select(x => x.Id)));
        }

        _assignableMetadata = GitHubMetadata.FromResponse(assignees);
      }

      return changes;
    }

    private async Task<IChangeSummary> UpdateLabels(ShipHubContext context, IGitHubPoolable github) {
      var changes = ChangeSummary.Empty;

      if (_labelMetadata.IsExpired()) {
        var labels = await github.Labels(_fullName, _labelMetadata);
        if (labels.IsOk) {
          changes = await context.BulkUpdateLabels(
            _repoId,
            labels.Result.Select(x => new LabelTableType() {
              Id = x.Id,
              Color = x.Color,
              Name = x.Name
            }),
            complete: true
          );
        }

        _labelMetadata = GitHubMetadata.FromResponse(labels);
      }

      return changes;
    }

    private async Task<IChangeSummary> UpdateMilestones(ShipHubContext context, IGitHubPoolable github) {
      var changes = ChangeSummary.Empty;

      if (_milestoneMetadata.IsExpired()) {
        var milestones = await github.Milestones(_fullName, _milestoneMetadata);
        if (milestones.IsOk) {
          changes = await context.BulkUpdateMilestones(_repoId, _mapper.Map<IEnumerable<MilestoneTableType>>(milestones.Result));
        }

        _milestoneMetadata = GitHubMetadata.FromResponse(milestones);
      }

      return changes;
    }

    private async Task<IChangeSummary> UpdateProjects(ShipHubContext context, IGitHubPoolable github) {
      var changes = new ChangeSummary();

      if (_projectMetadata.IsExpired()) {
        var projects = await github.RepositoryProjects(_fullName, _projectMetadata);
        if (projects.IsOk) {
          var creators = projects.Result.Select(p => new AccountTableType() {
            Type = Account.UserType,
            Login = p.Creator.Login,
            Id = p.Creator.Id
          }).Distinct(t => t.Id);
          if (creators.Any()) {
            changes.UnionWith(await context.BulkUpdateAccounts(projects.Date, creators));
          }
          await context.BulkUpdateRepositoryProjects(_repoId, _mapper.Map<IEnumerable<ProjectTableType>>(projects.Result));
        } else if (projects.Status == HttpStatusCode.Gone || projects.Status == HttpStatusCode.NotFound) {
          _projectsEnabled = false;
        }

        _projectMetadata = GitHubMetadata.FromResponse(projects);
      }

      return changes;
    }

    private async Task<IChangeSummary> UpdateIssues(ShipHubContext context, IGitHubPoolable github) {
      var changes = new ChangeSummary();

      if (_issueMetadata.IsExpired()) {
        var issueResponse = await github.Issues(_fullName, _issueSince, ChunkMaxPages, _issueMetadata);
        if (issueResponse.IsOk) {
          var issues = issueResponse.Result;

          var accounts = issues
            .SelectMany(x => new[] { x.User, x.ClosedBy }.Concat(x.Assignees))
            .Where(x => x != null)
            .Distinct(x => x.Id);

          // TODO: Store (hashes? modified date?) in this object and only apply changes.
          var milestones = issues
            .Select(x => x.Milestone)
            .Where(x => x != null)
            .Distinct(x => x.Id);

          changes.UnionWith(await context.BulkUpdateAccounts(issueResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(accounts)));
          changes.UnionWith(await context.BulkUpdateMilestones(_repoId, _mapper.Map<IEnumerable<MilestoneTableType>>(milestones)));
          changes.UnionWith(await context.BulkUpdateLabels(
            _repoId,
            issues.SelectMany(x => x.Labels?.Select(y => new LabelTableType() { Id = y.Id, Name = y.Name, Color = y.Color })).Distinct(x => x.Id)));
          changes.UnionWith(await context.BulkUpdateIssues(
            _repoId,
            _mapper.Map<IEnumerable<PullRequestTableType>>(issues),
            issues.SelectMany(x => x.Labels?.Select(y => new MappingTableType() { Item1 = x.Id, Item2 = y.Id })),
            issues.SelectMany(x => x.Assignees?.Select(y => new MappingTableType() { Item1 = x.Id, Item2 = y.Id }))));

          if (issues.Any()) {
            // Ensure we don't miss any when we hit the page limit.
            _issueSince = issues.Max(x => x.UpdatedAt).AddSeconds(-1);
            await context.UpdateRepositoryIssueSince(_repoId, _issueSince);
          }
        }

        _issueMetadata = GitHubMetadata.FromResponse(issueResponse);
      }

      return changes;
    }

    public async Task<IChangeSummary> AddOrUpdateWebhooks(ShipHubContext context, IGitHubRepositoryAdmin admin) {
      var changes = ChangeSummary.Empty;

      var hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.RepositoryId == _repoId);
      context.Database.Connection.Close();

      // If our last operation on this repo hook resulted in error, delay.
      if (hook?.LastError > DateTimeOffset.UtcNow.Subtract(HookErrorDelay)) {
        return changes; // Wait to try later.
      }

      // There are now a few cases to handle
      // If there is no record of a hook, try to make one.
      // If there is an incomplete record, try to make it.
      // If there is an errored record, sleep or retry
      if (hook?.GitHubId == null) {
        // GitHub will immediately send a ping when the webhook is created.
        // To avoid any chance for a race, add the Hook to the DB first, then
        // create on GitHub.
        HookTableType newHook = null;
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

        try {
          var hookList = await admin.RepositoryWebhooks(_fullName);
          if (!hookList.IsOk) {
            throw new Exception($"Unable to list hooks for {_fullName}");
          }

          var existingHooks = hookList.Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{_apiHostName}/", StringComparison.OrdinalIgnoreCase));

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in existingHooks) {
            var deleteResponse = await admin.DeleteRepositoryWebhook(_fullName, existingHook.Id);
            if (!deleteResponse.Succeeded) {
              Log.Info($"Failed to delete existing hook ({existingHook.Id}) for repo '{_fullName}'");
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
            changes = await context.BulkUpdateHooks(hooks: new[] { newHook });
          } else {
            throw new Exception($"Failed to add hook for repo '{_fullName}' ({_repoId}): {addRepoHookResponse.Status} {addRepoHookResponse.Error}");
          }
        } catch (Exception e) {
          e.Report();
          // Save LastError
          await context.BulkUpdateHooks(hooks: new[] { newHook });
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
          Log.Info($"Updating webhook {_fullName}/{hook.GitHubId} from [{hook.Events}] to [{string.Join(",", RequiredEvents)}]");
          var editResponse = await admin.EditRepositoryWebhookEvents(_fullName, (long)hook.GitHubId, RequiredEvents);

          if (editResponse.Succeeded) {
            editHook.LastError = null;
            editHook.GitHubId = editResponse.Result.Id;
            editHook.Events = string.Join(",", editResponse.Result.Events);
            await context.BulkUpdateHooks(hooks: new[] { editHook });
          } else if (editResponse.Status == HttpStatusCode.NotFound) {
            // Our record is out of date.
            Log.Info($"Failed to edit hook for repo '{_fullName}' ({_repoId}): {editResponse.Status} Deleting our hook record.");
            changes = await context.BulkUpdateHooks(deleted: new[] { editHook.Id });
          } else {
            throw new Exception($"Failed to edit hook for repo '{_fullName}' ({_repoId}): {editResponse.Status} {editResponse.Error}");
          }
        } catch (Exception e) {
          e.Report();
          // Save LastError
          await context.BulkUpdateHooks(hooks: new[] { editHook });
        }
      }

      return changes;
    }
  }
}
