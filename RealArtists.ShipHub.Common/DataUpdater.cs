namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using AutoMapper;
  using RealArtists.ShipHub.Common.DataModel;
  using RealArtists.ShipHub.Common.DataModel.Types;
  using g = GitHub.Models;

  public class DataUpdater {
    public const int LargeBatchSize = 500;

    private IFactory<ShipHubContext> _contextFactory;
    private IMapper _mapper;
    private ChangeSummary _changes = new ChangeSummary();

    public IChangeSummary Changes => _changes;

    // This is gross-ish
    public bool IssuesChanged { get; private set; }

    // This is gross too
    public void UnionWithExternalChanges(IChangeSummary changes) {
      if (changes != null && !changes.IsEmpty) {
        _changes.UnionWith(changes);
      }
    }

    public DataUpdater(IFactory<ShipHubContext> contextFactory, IMapper mapper) {
      _contextFactory = contextFactory;
      _mapper = mapper;
    }

    /// <summary>
    /// Only use this constructor if for short-lived DataUpdaters
    /// </summary>
    /// <param name="context">A borrowed context that the caller owns and will dispose of</param>
    /// <param name="mapper"></param>
    public DataUpdater(ShipHubContext context, IMapper mapper) {
      _borrowedContext = context;
      _mapper = mapper;
    }

    private ShipHubContext _borrowedContext;
    private ShipHubContext _ephemeralContext; // this is for WithContext only. If you're not WithContext, don't use it.
    private async Task WithContext(Func<ShipHubContext, Task> dbWork) {
      if (_borrowedContext != null) {
        // legacy path
        await dbWork(_borrowedContext);
      } else if (_ephemeralContext != null) {
        // re-entrant path
        await dbWork(_ephemeralContext);
      } else {
        try {
          _ephemeralContext = _contextFactory.CreateInstance();
          await dbWork(_ephemeralContext);
        } finally {
          if (_ephemeralContext != null) {
            _ephemeralContext.Dispose();
            _ephemeralContext = null;
          }
        }
      }
    }

    public async Task DeleteCommitComment(long commentId, DateTimeOffset? date) {
      await WithContext(async context => {
        _changes.UnionWith(await context.DeleteCommitComment(commentId, date));
      });
    }

    public async Task DeleteIssueComment(long commentId, DateTimeOffset? date) {
      await WithContext(async context => {
        _changes.UnionWith(await context.DeleteIssueComment(commentId, date));
      });
    }

    public async Task DeleteLabel(long labelId) {
      await WithContext(async context => {
        _changes.UnionWith(await context.DeleteLabel(labelId));
      });
    }

    public async Task DeleteMilestone(long milestoneId) {
      await WithContext(async context => {
        _changes.UnionWith(await context.DeleteMilestone(milestoneId));
      });
    }

    public async Task DeletePullRequestComment(long commentId, DateTimeOffset? date) {
      await WithContext(async context => {
        _changes.UnionWith(await context.DeletePullRequestComment(commentId, date));
      });
    }

    public async Task DeleteRepository(long repositoryId) {
      await WithContext(async context => {
        _changes.UnionWith(await context.DeleteRepositories(new[] { repositoryId }));
      });
    }

    public async Task DeleteReview(long reviewId) {
      await WithContext(async context => {
        _changes.UnionWith(await context.DeleteReview(reviewId));
      });
    }

    public async Task DisableRepository(long repositoryId) {
      await WithContext(async context => {
        _changes.UnionWith(await context.DisableRepository(repositoryId, true));
      });
    }

    public async Task MarkRepositoryIssuesAsFullyImported(long repositoryId) {
      await WithContext(async context => {
        _changes.UnionWith(await context.MarkRepositoryIssuesAsFullyImported(repositoryId));
      });
    }

    public async Task SetOrganizationAdmins(long organizationId, DateTimeOffset date, IEnumerable<g.Account> admins) {
      await WithContext(async context => {
        await UpdateAccounts(date, admins);

        // The code this replaced used a HashSet
        // I'm assuming that was to remove duplicates (sent by GitHub), so threw in a Distinct.
        var adminIds = admins.Select(x => x.Id).Distinct();
        _changes.UnionWith(await context.SetOrganizationAdmins(organizationId, adminIds));
      });
    }

    public async Task SetRepositoryAssignees(long repositoryId, DateTimeOffset date, IEnumerable<g.Account> assignees) {
      await WithContext(async context => {
        await UpdateAccounts(date, assignees);

        var assigneeIds = assignees.Select(x => x.Id);
        _changes.UnionWith(await context.SetRepositoryAssignableAccounts(repositoryId, assigneeIds));
      });
    }

    public async Task SetRepositoryIssueTemplate(long repositoryId, string issueTemplate, string pullRequestTemplate) {
      await WithContext(async context => {
        _changes.UnionWith(await context.SetRepositoryIssueTemplate(repositoryId, issueTemplate, pullRequestTemplate));
      });
    }

    public async Task SetUserOrganizations(long userId, DateTimeOffset date, IEnumerable<g.OrganizationMembership> memberships) {
      await WithContext(async context => {
        await UpdateAccounts(date, memberships.Select(x => x.Organization));
        _changes.UnionWith(await context.SetUserOrganizations(userId, memberships.Select(x => x.Organization.Id)));
      });
    }

    public async Task SetUserRepositories(long userId, DateTimeOffset date, IEnumerable<g.Repository> repositories) {
      var permissions = repositories
        .Select(x => new RepositoryPermissionsTableType() {
          RepositoryId = x.Id,
          Admin = x.Permissions.Admin,
          Pull = x.Permissions.Pull,
          Push = x.Permissions.Push,
        });

      await WithContext(async context => {
        await UpdateRepositories(date, repositories);
        _changes.UnionWith(await context.SetAccountLinkedRepositories(userId, permissions));
      });
    }

    public async Task UpdateAccounts(DateTimeOffset date, IEnumerable<g.Account> accounts) {
      if (!accounts.Any()) { return; }

      var mapped = _mapper.Map<IEnumerable<AccountTableType>>(accounts);

      await WithContext(async context => {
        _changes.UnionWith(await context.BulkUpdateAccounts(date, mapped));
      });
    }

    public async Task UpdateAccountMentionSince(long accountId, DateTimeOffset? mentionSince) {
      await WithContext(async context => {
        await context.UpdateAccountMentionSince(accountId, mentionSince);
      });
    }

    /// <summary>
    /// Updates the repositories we sync for a given account.
    /// </summary>
    /// <param name="accountId">The user's id.</param>
    /// <param name="autoTrack">The user's auto-track sync setting.</param>
    /// <param name="date">The date of the earliest public repo response. Used to update owner account info.</param>
    /// <param name="publicRepos">Public repos this user wishes to sync and that we may need to save. Need not be complete if unchanged. Can be null.</param>
    /// <param name="includeRepoMetadata">Repo ids and metadata (can be null for linked repos) the user explicitly wishes to include. Can be null.</param>
    /// <param name="excludeRepos">Repo ids the user explicitly wishes to exclude. Can be null.</param>
    /// <returns>The list of repos to sync for this user.</returns>
    public async Task<IDictionary<long, GitHubMetadata>> UpdateAccountSyncRepositories(
      long accountId,
      bool autoTrack,
      DateTimeOffset date,
      IEnumerable<g.Repository> publicRepos,
      IDictionary<long, GitHubMetadata> includeRepoMetadata,
      IEnumerable<long> excludeRepos) {

      var include = includeRepoMetadata
        ?.Select(x => new StringMappingTableType() {
          Key = x.Key,
          Value = x.Value?.SerializeObject(),
        })
        .ToArray();

      // This is gross.
      IDictionary<long, GitHubMetadata> result = null;

      await WithContext(async context => {
        if (publicRepos?.Any() == true) {
          await UpdateRepositories(date, publicRepos);
        }
        result = await context.UpdateAccountSyncRepositories(accountId, autoTrack, include, excludeRepos);
      });

      return result;
    }

    public async Task UpdateCommitComments(long repositoryId, DateTimeOffset date, IEnumerable<g.CommitComment> comments) {
      if (!comments.Any()) { return; }

      var authors = comments.Select(x => x.User).Distinct(x => x.Id);
      var mapped = _mapper.Map<IEnumerable<CommitCommentTableType>>(comments);

      await WithContext(async context => {
        await UpdateAccounts(date, authors);
        _changes.UnionWith(await context.BulkUpdateCommitComments(repositoryId, mapped));
      });
    }

    public async Task UpdateCommitStatuses(long repositoryId, string reference, IEnumerable<g.CommitStatus> statuses) {
      if (!statuses.Any()) { return; }

      var mappedStatuses = _mapper.Map<IEnumerable<CommitStatusTableType>>(statuses);

      await WithContext(async context => {
        _changes.UnionWith(await context.BulkUpdateCommitStatuses(repositoryId, reference, mappedStatuses));
      });
    }

    public async Task UpdateIssueComments(long repositoryId, DateTimeOffset date, IEnumerable<g.IssueComment> comments) {
      if (!comments.Any()) { return; }

      var authors = comments.Select(x => x.User).Distinct(x => x.Id);
      var mapped = _mapper.Map<IEnumerable<CommentTableType>>(comments);

      await WithContext(async context => {
        await UpdateAccounts(date, authors);
        _changes.UnionWith(await context.BulkUpdateIssueComments(repositoryId, mapped));
      });
    }

    public async Task UpdateIssues(long repositoryId, DateTimeOffset date, IEnumerable<g.Issue> issues) {
      if (!issues.Any()) { return; }

      var allAccounts = new List<g.Account>();
      foreach (var issue in issues) {
        allAccounts.Add(issue.User);
        if (issue.ClosedBy != null) { allAccounts.Add(issue.ClosedBy); }
        if (issue.Assignees?.Any() == true) {
          allAccounts.AddRange(issue.Assignees);
        }
      }
      var accounts = allAccounts.Where(x => x != null).Distinct(x => x.Id);

      var milestones = issues
          .Select(x => x.Milestone)
          .Where(x => x != null)
          .Distinct(x => x.Id);

      var labels = issues
          .SelectMany(x => x.Labels)
          .Distinct(x => x.Id);

      var issueArray = issues.ToArray();

      await WithContext(async context => {
        await UpdateAccounts(date, accounts);
        await UpdateMilestones(repositoryId, date, milestones);
        await UpdateLabels(repositoryId, labels);

        for (var i = 0; i < issueArray.Length; i += LargeBatchSize) {
          var segment = new ArraySegment<g.Issue>(issueArray, i, Math.Min(LargeBatchSize, issueArray.Length - i));

          var repoIssues = _mapper.Map<IEnumerable<IssueTableType>>(segment);
          var labelMap = segment.SelectMany(x => x.Labels.Select(y => new IssueMappingTableType() { IssueId = x.Id, IssueNumber = x.Number, MappedId = y.Id }));
          var assigneeMap = segment.SelectMany(x =>
            x.Assignees?.Select(y => new IssueMappingTableType() { IssueId = x.Id, IssueNumber = x.Number, MappedId = y.Id })
            ?? Array.Empty<IssueMappingTableType>());
          var issueChanges = await context.BulkUpdateIssues(repositoryId, repoIssues, labelMap, assigneeMap);

          if (!issueChanges.IsEmpty) {
            IssuesChanged = true;
            _changes.UnionWith(issueChanges);
          }
        }
      });
    }

    public async Task UpdateIssueMentions(long userId, IEnumerable<g.Issue> issues, bool complete = false) {
      IEnumerable<long> issueIds = Array.Empty<long>();
      if (issues?.Any() == true) {
        issueIds = issues.Select(x => x.Id).ToArray();
      }

      if (issueIds.Any() || complete) {
        await WithContext(async context => {
          _changes.UnionWith(await context.BulkUpdateIssueMentions(userId, issueIds, complete));
        });
      }
    }

    public async Task UpdateLabels(long repositoryId, IEnumerable<g.Label> labels, bool complete = false) {
      if (!labels.Any() && !complete) { return; }

      var repoLabels = _mapper.Map<IEnumerable<LabelTableType>>(labels);

      await WithContext(async context => {
        _changes.UnionWith(await context.BulkUpdateLabels(repositoryId, repoLabels, complete));
      });
    }

    public async Task UpdateMilestones(long repositoryId, DateTimeOffset date, IEnumerable<g.Milestone> milestones, bool complete = false) {
      if (!milestones.Any() && !complete) { return; }

      // Note: Milestones may have a Creator field, but we currently ignore it.

      var repoMilestones = _mapper.Map<IEnumerable<MilestoneTableType>>(milestones);

      await WithContext(async context => {
        _changes.UnionWith(await context.BulkUpdateMilestones(repositoryId, repoMilestones, complete));
      });
    }

    public async Task UpdateOrganizationProjects(long organizationId, DateTimeOffset date, IEnumerable<g.Project> projects) {
      // Note: All calls should have the complete list.
      // DB will remove any not provided
      var creators = projects.Select(x => x.Creator).Distinct(x => x.Id);
      var orgProjects = _mapper.Map<IEnumerable<ProjectTableType>>(projects);

      await WithContext(async context => {
        await UpdateAccounts(date, creators);
        _changes.UnionWith(await context.BulkUpdateOrganizationProjects(organizationId, orgProjects));
      });
    }

    public async Task UpdateRepositoryProjects(long repositoryId, DateTimeOffset date, IEnumerable<g.Project> projects) {
      // Note: All calls should have the complete list.
      // DB will remove any not provided
      var creators = projects.Select(x => x.Creator).Distinct(x => x.Id);
      var repoProjects = _mapper.Map<IEnumerable<ProjectTableType>>(projects);

      await WithContext(async context => {
        await UpdateAccounts(date, creators);
        _changes.UnionWith(await context.BulkUpdateRepositoryProjects(repositoryId, repoProjects));
      });
    }

    public async Task UpdatePullRequestComments(long repositoryId, long issueId, DateTimeOffset date, IEnumerable<g.PullRequestComment> comments, long? pendingReviewId = null, bool dropWithMissingReview = false) {
      if (!comments.Any()) { return; }

      var accounts = comments.Select(x => x.User).Distinct(x => x.Id).ToArray();
      var mappedComments = _mapper.Map<IEnumerable<PullRequestCommentTableType>>(comments);

      await WithContext(async context => {
        await UpdateAccounts(date, accounts);
        _changes.UnionWith(await context.BulkUpdatePullRequestComments(repositoryId, issueId, mappedComments, pendingReviewId, dropWithMissingReview));
      });
    }

    public async Task UpdatePullRequests(long repositoryId, DateTimeOffset date, IEnumerable<g.PullRequest> pullRequests) {
      if (!pullRequests.Any()) { return; }

      var allAccounts = new List<g.Account>();
      foreach (var pr in pullRequests) {
        allAccounts.Add(pr.User);
        if (pr.ClosedBy != null) { allAccounts.Add(pr.ClosedBy); }
        if (pr.MergedBy != null) { allAccounts.Add(pr.MergedBy); }
        if (pr.Assignees?.Any() == true) {
          allAccounts.AddRange(pr.Assignees);
        }
        if (pr.RequestedReviewers?.Any() == true) {
          allAccounts.AddRange(pr.RequestedReviewers);
        }
      }
      var accounts = allAccounts.Where(x => x != null).Distinct(x => x.Id);

      var milestones = pullRequests
        .Select(x => x.Milestone)
        .Where(x => x != null)
        .Distinct(x => x.Id)
        .ToArray();

      var prArray = pullRequests.ToArray();

      await WithContext(async context => {
        await UpdateAccounts(date, accounts);

        if (milestones.Any()) {
          await UpdateMilestones(repositoryId, date, milestones);
        }

        for (var i = 0; i < prArray.Length; i += LargeBatchSize) {
          var segment = new ArraySegment<g.PullRequest>(prArray, i, Math.Min(LargeBatchSize, prArray.Length - i));

          var prs = _mapper.Map<IEnumerable<PullRequestTableType>>(segment);
          var prReviewers = segment.SelectMany(x =>
            x.RequestedReviewers?.Select(y => new IssueMappingTableType(y.Id, x.Number))
            ?? Array.Empty<IssueMappingTableType>());
          _changes.UnionWith(await context.BulkUpdatePullRequests(repositoryId, prs, prReviewers));
        }
      });
    }

    public async Task UpdateRepositories(DateTimeOffset date, IEnumerable<g.Repository> repositories, bool enable = false) {
      var accounts = repositories.Select(x => x.Owner).Distinct(x => x.Id);

      var repos = _mapper.Map<IEnumerable<RepositoryTableType>>(repositories);

      // This method should only enable repos, *never* disable.
      // That's handled by ShipHubContext.DisableRepository
      if (enable) {
        foreach (var repo in repos) {
          repo.Disabled = false;
        }
      }

      await WithContext(async context => {
        await UpdateAccounts(date, accounts);
        _changes.UnionWith(await context.BulkUpdateRepositories(date, repos));
      });
    }

    public async Task UpdateRepositoryCommentSince(long repositoryId, DateTimeOffset? commentSince) {
      await WithContext(async context => {
        await context.UpdateRepositoryCommentSince(repositoryId, commentSince);
      });
    }

    public async Task UpdateRepositoryIssueSince(long repositoryId, DateTimeOffset? issueSince) {
      await WithContext(async context => {
        await context.UpdateRepositoryIssueSince(repositoryId, issueSince);
      });
    }

    public async Task UpdateRepositoryReviewVersion(long repositoryId, long? version) {
      await WithContext(async context => {
        await context.UpdateRepositoryReviewVersion(repositoryId, version);
      });
    }

    public async Task UpdateReviews(long repositoryId, long issueId, DateTimeOffset date, IEnumerable<g.Review> reviews, long? userId = null, bool complete = false) {
      if (!reviews.Any()) { return; }

      var accounts = reviews.Select(x => x.User).Distinct(x => x.Id).ToArray();
      var mappedReviews = _mapper.Map<IEnumerable<ReviewTableType>>(reviews);

      await WithContext(async context => {
        await UpdateAccounts(date, accounts);
        _changes.UnionWith(await context.BulkUpdateReviews(repositoryId, issueId, date, mappedReviews, userId, complete));
      });
    }

    public async Task UpdateReviews(long repositoryId, DateTimeOffset date, IEnumerable<(long IssueId, IEnumerable<g.Review> Reviews)> reviews) {
      if (!reviews.Any()) { return; }

      var accounts = reviews.SelectMany(x => x.Reviews.Select(y => y.User)).Distinct(x => x.Id).ToArray();

      await WithContext(async context => {
        await UpdateAccounts(date, accounts);

        // TODO: Batch this.
        foreach (var elem in reviews) {
          var mappedReviews = _mapper.Map<IEnumerable<ReviewTableType>>(elem.Reviews);
          _changes.UnionWith(await context.BulkUpdateReviews(repositoryId, elem.IssueId, date, mappedReviews));
        }
      });
    }

    public async Task UpdateTimelineEvents(long repositoryId, DateTimeOffset date, long forUserId, IEnumerable<g.Account> extraReferencedAccounts, IEnumerable<g.IssueEvent> events) {
      if (!events.Any()) { return; }

      // This conversion handles the restriction field and hash.
      var mappedEvents = _mapper.Map<IEnumerable<IssueEventTableType>>(events);

      // Just in case
      var uniqueAccounts = extraReferencedAccounts.Where(x => x != null).Distinct(x => x.Id).ToArray();

      await WithContext(async context => {
        await UpdateAccounts(date, uniqueAccounts);
        _changes.UnionWith(await context.BulkUpdateTimelineEvents(forUserId, repositoryId, mappedEvents, uniqueAccounts.Select(x => x.Id)));
      });
    }

    public Task UpdateIssueReactions(long repositoryId, DateTimeOffset date, long issueId, IEnumerable<g.Reaction> reactions) {
      return UpdateReactions(repositoryId, date, reactions, issueId: issueId);
    }

    public Task UpdateIssueCommentReactions(long repositoryId, DateTimeOffset date, long issueCommentId, IEnumerable<g.Reaction> reactions) {
      return UpdateReactions(repositoryId, date, reactions, issueCommentId: issueCommentId);
    }

    public Task UpdateCommitCommentReactions(long repositoryId, DateTimeOffset date, long commitCommentId, IEnumerable<g.Reaction> reactions) {
      return UpdateReactions(repositoryId, date, reactions, commitCommentId: commitCommentId);
    }

    public Task UpdatePullRequestCommentReactions(long repositoryId, DateTimeOffset date, long pullRequestCommentId, IEnumerable<g.Reaction> reactions) {
      return UpdateReactions(repositoryId, date, reactions, pullRequestCommentId: pullRequestCommentId);
    }

    public async Task UpdateReactions(
      long repositoryId,
      DateTimeOffset date,
      IEnumerable<g.Reaction> reactions,
      long? issueId = null,
      long? issueCommentId = null,
      long? commitCommentId = null,
      long? pullRequestCommentId = null) {
      if (!reactions.Any()) { return; }

      var accounts = reactions.Select(x => x.User).Distinct(x => x.Id).ToArray();
      var mappedReactions = _mapper.Map<IEnumerable<ReactionTableType>>(reactions);

      await WithContext(async context => {
        await UpdateAccounts(date, accounts);
        _changes.UnionWith(await context.BulkUpdateReactions(repositoryId, mappedReactions, issueId, issueCommentId, commitCommentId, pullRequestCommentId));
      });
    }

    public async Task ForceResyncRepositoryIssues(long repositoryId) {
      await WithContext(async context => {
        _changes.UnionWith(await context.ForceResyncRepositoryIssues(repositoryId));
      });
    }

    public async Task ToggleWatchQuery(Guid queryId, long watcherId, bool watch) {
      await WithContext(async context => {
        _changes.UnionWith(await context.ToggleWatchQuery(queryId, watcherId, watch));
      });
    }

    public async Task UpdateQuery(Guid queryId, long authorId, string title, string predicate) {
      await WithContext(async context => {
        _changes.UnionWith(await context.UpdateQuery(queryId, authorId, title, predicate));
      });
    }
  }
}
