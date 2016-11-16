namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using ActorInterfaces;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using GitHub;
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
    private GitHubMetadata _issueMetadata;
    private GitHubMetadata _labelMetadata;
    private GitHubMetadata _milestoneMetadata;
    private GitHubMetadata _contentsRootMetadata;
    private GitHubMetadata _contentsDotGithubMetadata;
    private GitHubMetadata _contentsIssueTemplateMetadata;

    // Sync logic
    private DateTimeOffset _lastSyncInterest;
    private IDisposable _syncTimer;

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
        _issueMetadata = repo.IssueMetadata;
        _labelMetadata = repo.LabelMetadata;
        _milestoneMetadata = repo.MilestoneMetadata;
        _contentsRootMetadata = repo.ContentsRootMetadata;
        _contentsDotGithubMetadata = repo.ContentsDotGitHubMetadata;
        _contentsIssueTemplateMetadata = repo.ContentsIssueTemplateMetadata;
      }

      await base.OnActivateAsync();
    }

    public override async Task OnDeactivateAsync() {
      using (var context = _contextFactory.CreateInstance()) {
        // I think all we need to persist is the metadata.
        await context.UpdateMetadata("Repositories", _repoId, _metadata);
        await context.UpdateMetadata("Repositories", "AssignableMetadataJson", _repoId, _assignableMetadata);
        await context.UpdateMetadata("Repositories", "IssueMetadataJson", _repoId, _issueMetadata);
        await context.UpdateMetadata("Repositories", "LabelMetadataJson", _repoId, _labelMetadata);
        await context.UpdateMetadata("Repositories", "MilestoneMetadataJson", _repoId, _milestoneMetadata);
        await context.UpdateMetadata("Repositories", "ContentsRootMetadataJson", _repoId, _contentsRootMetadata);
        await context.UpdateMetadata("Repositories", "ContentsDotGithubMetadataJson", _repoId, _contentsDotGithubMetadata);
        await context.UpdateMetadata("Repositories", "ContentsIssueTemplateMetadataJson", _repoId, _contentsIssueTemplateMetadata);
      }

      // TODO: Look into how agressively Orleans deactivates "inactive" grains.
      // We may need to delay deactivation based on sync interest.

      await base.OnDeactivateAsync();
    }

    public Task Sync(long forUserId) {
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
      using (var context = _contextFactory.CreateInstance()) {
        var syncUserIds = await context.AccountRepositories
          .Where(x => x.RepositoryId == _repoId)
          .Where(x => x.Account.Token != null)
          .Select(x => x.AccountId)
          .ToArrayAsync();

        if (syncUserIds.Length == 0) {
          DeactivateOnIdle();
          return;
        }

        var github = new GitHubActorPool(_grainFactory, syncUserIds);

        // Update repo
        if (_metadata == null || _metadata.Expires < DateTimeOffset.UtcNow) {
          var repo = await github.Repository(_fullName, _metadata);

          if (repo.Status != HttpStatusCode.NotModified) {
            changes.UnionWith(
              await context.BulkUpdateRepositories(repo.Date, _mapper.Map<IEnumerable<RepositoryTableType>>(new[] { repo.Result }))
            );
          }

          // Don't update until saved.
          _metadata = GitHubMetadata.FromResponse(repo);
        }

        var issueTemplateChanges = await UpdateIssueTemplate(context, github);
        changes.UnionWith(issueTemplateChanges);

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
        if (_milestoneMetadata == null || _milestoneMetadata.Expires < DateTimeOffset.UtcNow) {
          var milestones = await github.Milestones(_fullName, _milestoneMetadata);
          if (milestones.Status != HttpStatusCode.NotModified) {
            changes.UnionWith(
              await context.BulkUpdateMilestones(_repoId, _mapper.Map<IEnumerable<MilestoneTableType>>(milestones.Result))
            );
          }

          _milestoneMetadata = GitHubMetadata.FromResponse(milestones);
        }

        // Update Issues
        // TODO: Do this incrementally (since, or skipping to last page, etc)
        if (_issueMetadata == null || _issueMetadata.Expires < DateTimeOffset.UtcNow) {
          var issueResponse = await github.Issues(_fullName, null, _issueMetadata);
          if (issueResponse.Status != HttpStatusCode.NotModified) {
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

            changes.UnionWith(
              await context.BulkUpdateAccounts(issueResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(accounts)),
              await context.BulkUpdateMilestones(_repoId, _mapper.Map<IEnumerable<MilestoneTableType>>(milestones)),
              await context.BulkUpdateIssues(
                _repoId,
                _mapper.Map<IEnumerable<IssueTableType>>(issues),
                issues.SelectMany(x => x.Labels?.Select(y => new LabelTableType() { ItemId = x.Id, Color = y.Color, Name = y.Name })),
                issues.SelectMany(x => x.Assignees?.Select(y => new MappingTableType() { Item1 = x.Id, Item2 = y.Id }))
            ));
          }

          _issueMetadata = GitHubMetadata.FromResponse(issueResponse);
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

      // Send Changes.
      if (!changes.Empty) {
        tasks.Add(_queueClient.NotifyChanges(changes));
      }

      // Await all outstanding operations.
      await Task.WhenAll(tasks);
    }

    static Regex IssueTemplateRegex = new Regex("^issue_template(?:\\.\\w+)?$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));

    private static bool IsTemplateFile(Common.GitHub.Models.ContentsFile file) {
      return file.Type == Common.GitHub.Models.ContentsFileType.File
            && file.Name != null
            && IssueTemplateRegex.IsMatch(file.Name);
    }

    private async Task<ChangeSummary> UpdateIssueTemplate(ShipHubContext context, GitHubActorPool github) {
      var prevRootMetadata = _contentsRootMetadata;
      var prevDotGitHubMetadata = _contentsDotGithubMetadata;
      var prevIssueTemplateMetadata = _contentsIssueTemplateMetadata;
      try {
        return await _UpdateIssueTemplate(context, github);
      } catch {
        // unwind anything we've discovered about the cache state if we couldn't finish the whole operation
        _contentsRootMetadata = prevRootMetadata;
        _contentsDotGithubMetadata = prevDotGitHubMetadata;
        _contentsIssueTemplateMetadata = prevIssueTemplateMetadata;
        throw;
      }
    }

    private async Task<ChangeSummary> _UpdateIssueTemplate(ShipHubContext context, GitHubActorPool github) {
      if (_contentsIssueTemplateMetadata == null) {
        // then we don't have any known IssueTemplate, we have to search for it
        var rootListing = await github.ListDirectoryContents(_fullName, "/", _contentsRootMetadata);
        _contentsRootMetadata = GitHubMetadata.FromResponse(rootListing);
        if (rootListing.Status == HttpStatusCode.OK) {
          // search the root listing for any matching ISSUE_TEMPLATE files
          var rootTemplateFile = rootListing.Result.FirstOrDefault(IsTemplateFile);
          Common.GitHub.GitHubResponse<byte[]> templateContent = null;

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
              _contentsDotGithubMetadata = GitHubMetadata.FromResponse(dotGitHubListing);
              if (dotGitHubListing.Status == HttpStatusCode.OK) {
                var dotGitHubTemplateFile = dotGitHubListing.Result.FirstOrDefault(IsTemplateFile);
                if (dotGitHubTemplateFile != null) {
                  templateContent = await github.FileContents(_fullName, dotGitHubTemplateFile.Path);
                }
              }
            }
          } else /* rootTemplateFile != null */ {
            templateContent = await github.FileContents(_fullName, rootTemplateFile.Path);
          }

          _contentsIssueTemplateMetadata = GitHubMetadata.FromResponse(templateContent);
          if (templateContent != null && templateContent.Status == HttpStatusCode.OK) {
            return await UpdateIssueTemplateWithResult(context, templateContent.Result);
          }
        }
        return new ChangeSummary(); // couldn't find any ISSUE_TEMPLATE anywhere
      } else {
        // we have some cached data on an existing ISSUE_TEMPLATE.
        // try to short-circuit by just looking it up.
        // If we get a 404, then we start over from the top
        var filePath = _contentsIssueTemplateMetadata.Path.Substring($"repos/{_fullName}/contents".Length);
        var templateContent = await github.FileContents(_fullName, _contentsIssueTemplateMetadata.Path);
        _contentsIssueTemplateMetadata = GitHubMetadata.FromResponse(templateContent);
        if (templateContent.Status == HttpStatusCode.NotFound) {
          // it's been deleted or moved. clear it optimistically, but then start a new search from the top.
          var changes = new ChangeSummary();
          changes.UnionWith(await UpdateIssueTemplateWithResult(context, null));
          changes.UnionWith(await _UpdateIssueTemplate(context, github));
          return changes;
        } else if (templateContent.Status == HttpStatusCode.OK) {
          return await UpdateIssueTemplateWithResult(context, templateContent.Result);
        } else {
          return new ChangeSummary(); // nothing changed as far as we can tell
        }
     }
    }

    private Task<ChangeSummary> UpdateIssueTemplateWithResult(ShipHubContext context, byte[] templateData) {
      string templateStr = null;
      if (templateData != null) {
        templateStr = System.Text.Encoding.UTF8.GetString(templateData);
      }

      return context.SetRepositoryIssueTemplate(_repoId, templateStr);
    }
  }
}
