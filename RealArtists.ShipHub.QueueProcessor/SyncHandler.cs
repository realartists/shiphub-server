namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics;
  using System.Linq;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Microsoft.Azure.WebJobs;
  using Newtonsoft.Json.Linq;
  using QueueClient;
  using QueueClient.Messages;
  using gm = Common.GitHub.Models;

  public static class SyncHandlerExtensions {
    public static Task Send(this IAsyncCollector<ChangeMessage> topic, ChangeSummary summary) {
      if (summary != null && !summary.Empty) {
        return topic.AddAsync(new ChangeMessage(summary));
      }
      return Task.CompletedTask;
    }
  }

  /// <summary>
  /// TODO: ENSURE PARTITIONING AND MESSAGE IDS ARE SET CORRECTLY.
  /// TODO: Incremental sync when possible
  /// TODO: Don't submit empty updates to DB.
  /// </summary>

  public static class SyncHandler {
    /// <summary>
    /// Precondition: None.
    /// Postcondition: User saved in DB.
    /// </summary>
    public static async Task SyncAccount(
      [ServiceBusTrigger(ShipHubQueueNames.SyncAccount)] AccessTokenMessage message,
      [ServiceBus(ShipHubQueueNames.SyncAccountRepositories)] IAsyncCollector<AccountMessage> syncAccountRepos,
      [ServiceBus(ShipHubQueueNames.SyncAccountOrganizations)] IAsyncCollector<AccountMessage> syncAccountOrgs,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var userResponse = await ghc.User();
      var user = userResponse.Result;
      using (var context = new ShipHubContext()) {
        changes = await context.BulkUpdateAccounts(userResponse.Date, new[] { SharedMapper.Map<AccountTableType>(user) });
      }

      // Now that the user is saved in the DB, safe to sync all repos and user's orgs
      var am = new AccountMessage() {
        AccessToken = message.AccessToken,
        Account = user,
      };

      await Task.WhenAll(
        changes.Empty ? Task.CompletedTask : notifyChanges.AddAsync(new ChangeMessage(changes)),
        syncAccountRepos.AddAsync(am),
        syncAccountOrgs.AddAsync(am));
    }

    /// <summary>
    /// Precondition: User saved in DB.
    /// Postcondition: User's repos, their owners, and user's repo-links saved in DB.
    /// </summary>
    public static async Task SyncAccountRepositories(
      [ServiceBusTrigger(ShipHubQueueNames.SyncAccountRepositories)] AccountMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepository)] IAsyncCollector<RepositoryMessage> syncRepo,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var repoResponse = await ghc.Repositories();
      var reposWithIssues = repoResponse.Result.Where(x => x.HasIssues);
      var assignableRepos = reposWithIssues.ToDictionary(x => x.FullName, x => ghc.IsAssignable(x.FullName, message.Account.Login));
      await Task.WhenAll(assignableRepos.Values.ToArray());
      var keepRepos = reposWithIssues.Where(x => assignableRepos[x.FullName].Result.Result).ToArray();

      using (var context = new ShipHubContext()) {
        var owners = keepRepos
          .Select(x => x.Owner)
          .GroupBy(x => x.Login)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(repoResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(owners));
        changes.UnionWith(await context.BulkUpdateRepositories(repoResponse.Date, SharedMapper.Map<IEnumerable<RepositoryTableType>>(keepRepos)));
        changes.UnionWith(await context.SetAccountLinkedRepositories(message.Account.Id, keepRepos.Select(x => x.Id), GitHubMetadata.FromResponse(repoResponse)));
      }

      // Now that owners, repos, and links are saved, safe to sync the repos themselves.
      var syncTasks = keepRepos.Select(x => syncRepo.AddAsync(new RepositoryMessage() {
        AccessToken = message.AccessToken,
        Repository = x,
      })).ToList();

      if (!changes.Empty) {
        syncTasks.Add(notifyChanges.AddAsync(new ChangeMessage(changes)));
      }

      await Task.WhenAll(syncTasks);
    }

    ///
    /// NOTE WELL: We sync only sync orgs for which the user is a member. If they can see a repo in an org
    /// but aren't a member, too bad for them. The permissions are too painful otherwise.
    ///

    /// <summary>
    /// Syncs the list of organizations of which the account is a member.
    /// Precondition: User exists
    /// Postcondition: User's organizations exist
    /// </summary>
    public static async Task SyncAccountOrganizations(
      [ServiceBusTrigger(ShipHubQueueNames.SyncAccountOrganizations)] AccountMessage message,
      [ServiceBus(ShipHubQueueNames.SyncOrganizationMembers)] IAsyncCollector<AccountMessage> syncOrgMembers,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var orgResponse = await ghc.Organizations();
      var orgs = orgResponse.Result;

      using (var context = new ShipHubContext()) {
        changes = await context.BulkUpdateAccounts(orgResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(orgs));
        changes.UnionWith(await context.SetUserOrganizations(message.Account.Id, orgs.Select(x => x.Id)));
      }

      var memberSyncMessages = orgs.
        Select(x => syncOrgMembers.AddAsync(new AccountMessage() {
          AccessToken = message.AccessToken,
          Account = x,
        })).ToList();

      if (!changes.Empty) {
        memberSyncMessages.Add(notifyChanges.AddAsync(new ChangeMessage(changes)));
      }

      await Task.WhenAll(memberSyncMessages);
    }

    /// <summary>
    /// Precondition: Organizations exist.
    /// Postcondition: Org members exist and Org membership is up to date.
    /// </summary>
    public static async Task SyncOrganizationMembers(
      [ServiceBusTrigger(ShipHubQueueNames.SyncOrganizationMembers)] AccountMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var memberResponse = await ghc.OrganizationMembers(message.Account.Login);
      var members = memberResponse.Result;

      using (var context = new ShipHubContext()) {
        changes = await context.BulkUpdateAccounts(memberResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(members));
        changes.UnionWith(await context.SetOrganizationUsers(message.Account.Id, members.Select(x => x.Id)));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    /// <summary>
    /// Precondition: Repos saved in DB
    /// Postcondition: None.
    /// </summary>
    /// TODO: Should this be inlined?
    public static async Task SyncRepository(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepository)] RepositoryMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryAssignees)] IAsyncCollector<RepositoryMessage> syncRepoAssignees,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryMilestones)] IAsyncCollector<RepositoryMessage> syncRepoMilestones,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryLabels)] IAsyncCollector<RepositoryMessage> syncRepoLabels) {
      // This is just a fanout point.
      // Plan to add conditional checks here to reduce polling frequency.
      await Task.WhenAll(
        syncRepoAssignees.AddAsync(message),
        syncRepoMilestones.AddAsync(message),
        syncRepoLabels.AddAsync(message)
      );
    }

    /// <summary>
    /// Precondition: Repository exists
    /// Postcondition: Repository assignees exist and are linked.
    /// </summary>
    public static async Task SyncRepositoryAssignees(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryAssignees)] RepositoryMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var assigneeResponse = await ghc.Assignable(message.Repository.FullName);
      var assignees = assigneeResponse.Result;

      using (var context = new ShipHubContext()) {
        changes = await context.BulkUpdateAccounts(assigneeResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(assignees));
        changes.UnionWith(await context.SetRepositoryAssignableAccounts(message.Repository.Id, assignees.Select(x => x.Id)));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    /// <summary>
    /// Precondition: Repository exists
    /// Postcondition: Milestones exist
    /// </summary>
    public static async Task SyncRepositoryMilestones(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryMilestones)] RepositoryMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssues)] IAsyncCollector<RepositoryMessage> syncRepoIssues,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var milestoneResponse = await ghc.Milestones(message.Repository.FullName);
      var milestones = milestoneResponse.Result;

      using (var context = new ShipHubContext()) {
        changes = await context.BulkUpdateMilestones(message.Repository.Id, SharedMapper.Map<IEnumerable<MilestoneTableType>>(milestones));
      }

      await Task.WhenAll(
        changes.Empty ? Task.CompletedTask : notifyChanges.AddAsync(new ChangeMessage(changes)),
        syncRepoIssues.AddAsync(message));
    }

    /// <summary>
    /// Precondition: Repository exists
    /// Postcondition: Labels exist
    /// </summary>
    public static async Task SyncRepositoryLabels(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryLabels)] RepositoryMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var labelResponse = await ghc.Labels(message.Repository.FullName);
      var labels = labelResponse.Result;

      using (var context = new ShipHubContext()) {
        changes = await context.SetRepositoryLabels(
          message.Repository.Id,
          labels.Select(x => new LabelTableType() {
            ItemId = message.Repository.Id,
            Color = x.Color,
            Name = x.Name
          })
        );
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    /// <summary>
    /// Precondition: Repository and Milestones exist
    /// Postcondition: Issues exist
    /// </summary>
    public static async Task SyncRepositoryIssues(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssues)] RepositoryMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryComments)] IAsyncCollector<RepositoryMessage> syncRepoComments,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueEvents)] IAsyncCollector<RepositoryMessage> syncRepoIssueEvents,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var issueResponse = await ghc.Issues(message.Repository.FullName);
      var issues = issueResponse.Result;

      using (var context = new ShipHubContext()) {
        // TODO: Support multiple assignees.
        var accounts = issues
          .SelectMany(x => new[] { x.User, x.Assignee, x.ClosedBy })
          .Where(x => x != null)
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(issueResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(accounts));

        var milestones = issues
          .Select(x => x.Milestone)
          .Where(x => x != null)
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        changes.UnionWith(await context.BulkUpdateMilestones(message.Repository.Id, SharedMapper.Map<IEnumerable<MilestoneTableType>>(milestones)));

        // TODO: Support multiple assignees.
        changes.UnionWith(await context.BulkUpdateIssues(
          message.Repository.Id,
          SharedMapper.Map<IEnumerable<IssueTableType>>(issues),
          issues.SelectMany(x => x.Labels.Select(y => new LabelTableType() { ItemId = x.Id, Color = y.Color, Name = y.Name }))));
      }

      await Task.WhenAll(
        changes.Empty ? Task.CompletedTask : notifyChanges.AddAsync(new ChangeMessage(changes)),
        syncRepoComments.AddAsync(message),
        syncRepoIssueEvents.AddAsync(message));
    }

    public static async Task SyncRepositoryComments(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryComments)] RepositoryMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var commentsResponse = await ghc.Comments(message.Repository.FullName);
      var comments = commentsResponse.Result;

      using (var context = new ShipHubContext()) {
        var users = comments
          .Select(x => x.User)
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(commentsResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(users));

        var issueComments = comments.Where(x => x.IssueNumber != null);
        changes.UnionWith(await context.BulkUpdateComments(message.Repository.Id, SharedMapper.Map<IEnumerable<CommentTableType>>(issueComments), complete: true));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    public static async Task SyncRepositoryIssueEvents(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueEvents)] RepositoryMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var eventsResponse = await ghc.Events(message.Repository.FullName);
      var events = eventsResponse.Result;

      using (var context = new ShipHubContext()) {
        // TODO: Gross
        var userId = await context.Users
          .Where(x => x.Token == message.AccessToken)
          .Select(x => x.Id)
          .SingleAsync();

        // For now only grab accounts from the response.
        // Sometimes an issue is also included, but not always, and we get them elsewhere anyway.
        var accounts = events
          .SelectMany(x => new[] { x.Actor, x.Assignee, x.Assigner })
          .Where(x => x != null)
          .GroupBy(x => x.Login)
          .Select(x => x.First());
        var accountsParam = SharedMapper.Map<IEnumerable<AccountTableType>>(accounts);
        changes = await context.BulkUpdateAccounts(eventsResponse.Date, accountsParam);
        var eventsParam = SharedMapper.Map<IEnumerable<IssueEventTableType>>(events);
        changes.UnionWith(await context.BulkUpdateIssueEvents(userId, message.Repository.Id, eventsParam, accountsParam.Select(x => x.Id)));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    public static async Task SyncRepositoryIssueComments(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueComments)] IssueMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var commentsResponse = await ghc.Comments(message.RepositoryFullName, message.Number);
      var comments = commentsResponse.Result;

      using (var context = new ShipHubContext()) {
        var users = comments
          .Select(x => x.User)
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(commentsResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(users));

        changes.UnionWith(await context.BulkUpdateIssueComments(
          message.RepositoryFullName,
          message.Number,
          SharedMapper.Map<IEnumerable<CommentTableType>>(comments),
          complete: true));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    private static HashSet<string> _FilterEvents = new HashSet<string>(new[] { "commented", "subscribed", "unsubscribed" }, StringComparer.OrdinalIgnoreCase);
    public static async Task SyncRepositoryIssueTimeline(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueTimeline)] IssueMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueComments)] IAsyncCollector<IssueMessage> syncIssueComments,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      ///////////////////////////////////////////
      /* NOTE!
       * We can't sync the timeline incrementally, because the client wants commit and
       * reference data inlined. This means we always have to download all the
       * timeline events in case an old one now has updated data. Other options are to
       * just be wrong, or to simply reference the user by id and mark them referenced
       * by the repo.
       */
      //////////////////////////////////////////

      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;
      var tasks = new List<Task>();

      var timelineResponse = await ghc.Timeline(message.RepositoryFullName, message.Number);
      var timeline = timelineResponse.Result;

      // If we find comments, sync them
      // TODO: Incrementally
      if (timeline.Any(x => x.Event == "commented")) {
        tasks.Add(syncIssueComments.AddAsync(message));
      }

      // Now just filter
      var filteredEvents = timeline.Where(x => !_FilterEvents.Contains(x.Event)).ToArray();

      using (var context = new ShipHubContext()) {
        // TODO: Gross
        var userId = await context.Users
          .Where(x => x.Token == message.AccessToken)
          .Select(x => x.Id)
          .SingleAsync();

        // TODO: I don't like this
        var issueDetails = await context.Issues
          .Where(x => x.Repository.FullName == message.RepositoryFullName)
          .Where(x => x.Number == message.Number)
          .Select(x => new { IssueId = x.Id, RepositoryId = x.RepositoryId })
          .SingleAsync();

        // For adding to the DB later
        var accounts = new List<gm.Account>();
        foreach (var tl in filteredEvents) {
          accounts.Add(tl.Actor);
          accounts.Add(tl.Assignee);
          accounts.Add(tl.Source?.Actor);
        }

        // Find all events with associated commits, and embed them.
        var withCommits = filteredEvents.Where(x => !string.IsNullOrWhiteSpace(x.CommitUrl)).ToArray();
        var commits = withCommits.Select(x => x.CommitUrl).Distinct();

        if (commits.Any()) {
          var commitLookups = commits
            .Select(x => {
              var parts = x.Split('/');
              var numParts = parts.Length;
              var repoName = parts[numParts - 4] + "/" + parts[numParts - 3];
              var sha = parts[numParts - 1];
              return new {
                Id = x,
                Task = ghc.Commit(repoName, sha),
              };
            })
            .ToDictionary(x => x.Id, x => x.Task);

          // TODO: Lookup Repo Name->ID mapping

          await Task.WhenAll(commitLookups.Values);

          foreach (var item in withCommits) {
            var lookup = commitLookups[item.CommitUrl];
            var commit = lookup.Result.Result;
            accounts.Add(commit.Author);
            accounts.Add(commit.Committer);
            item.ExtensionDataDictionary["ship_commit_message"] = commit.CommitDetails.Message;
            item.ExtensionDataDictionary["ship_commit_author"] = new JObject(commit.Author);
            item.ExtensionDataDictionary["ship_commit_committer"] = new JObject(commit.Committer);
          }
        }

        var withSources = filteredEvents.Where(x => x.Source != null).ToArray();
        var sources = withSources.Select(x => x.Source.IssueUrl).Distinct();

        if (sources.Any()) {
          var sourceLookups = sources
            .Select(x => {
              var parts = x.Split('/');
              var numParts = parts.Length;
              var repoName = parts[numParts - 4] + "/" + parts[numParts - 3];
              var issueNum = int.Parse(parts[numParts - 1]);
              return new {
                Id = x,
                Task = ghc.Issue(repoName, issueNum),
              };
            })
            .ToDictionary(x => x.Id, x => x.Task);

          await Task.WhenAll(sourceLookups.Values);

          var prLookups = sourceLookups.Values
            .Where(x => x.Result.Result.PullRequest != null)
            .Select(x => {
              var parts = x.Result.Result.PullRequest.Url.Split('/');
              var numParts = parts.Length;
              var repoName = parts[numParts - 4] + "/" + parts[numParts - 3];
              var prNum = int.Parse(parts[numParts - 1]);
              return new {
                Id = "",
                Task = ghc.PullRequest(repoName, prNum),
              };
            })
            .ToDictionary(x => x.Id, x => x.Task);

          await Task.WhenAll(prLookups.Values);

          foreach (var item in withSources) {
            var issue = sourceLookups[item.Source.IssueUrl].Result.Result;
            accounts.Add(item.Source.Actor);
            accounts.Add(issue.Assignee);
            accounts.AddRange(issue.Assignees); // Do we need both assignee and assignees? I think yes.
            accounts.Add(issue.ClosedBy);
            accounts.Add(issue.User);

            item.ExtensionDataDictionary["ship_issue_state"] = issue.State;
            item.ExtensionDataDictionary["ship_issue_title"] = issue.Title;

            if (issue.PullRequest != null) {
              var pr = prLookups[issue.PullRequest.Url].Result.Result;
              item.ExtensionDataDictionary["ship_pull_request_merged"] = pr.Merged;
            }
          }
        }

        // Update accounts
        var uniqueAccounts = accounts
          .Where(x => x != null)
          .GroupBy(x => x.Login)
          .Select(x => x.First());
        var accountsParam = SharedMapper.Map<IEnumerable<AccountTableType>>(uniqueAccounts);
        changes = await context.BulkUpdateAccounts(timelineResponse.Date, accountsParam);

        // Cleanup the data
        foreach (var item in filteredEvents) {
          // Oh GitHub, how I hate thee. Why can't you provide ids?
          // We're regularly seeing GitHub ids as large as 31 bits.
          // We can only store four things this way because we only have two free bits :(
          // TODO: HACK! THIS IS BRITTLE AND WILL BREAK!
          var ones31 = 0x7FFFFFFFL;
          var issuePart = (issueDetails.IssueId & ones31);
          if (issuePart != issueDetails.IssueId) {
            throw new NotSupportedException($"IssueId {issueDetails.IssueId} exceeds 31 bits!");
          }
          switch (item.Event) {
            case "cross-referenced":
              // high bits 11
              var commentPart = (item.Source.CommentId & ones31);
              if (commentPart != item.Source.CommentId) {
                throw new NotSupportedException($"CommentId {item.Source.CommentId} exceeds 31 bits!");
              }
              item.Id = ((long)3) << 62 | commentPart << 31 | issuePart;
              item.Actor = item.Source.Actor;
              break;
            case "committed":
              // high bits 10
              var sha = item.ExtensionDataDictionary["sha"].ToObject<string>();
              var shaBytes = SoapHexBinary.Parse(sha).Value;
              var shaPart = BitConverter.ToInt64(shaBytes, 0) & ones31;
              item.Id = ((long)2) << 62 | shaPart << 31 | issuePart;
              item.CreatedAt = item.ExtensionDataDictionary["committer"]["date"].ToObject<DateTimeOffset>();
              break;
            default:
              break;
          }

#if DEBUG
          // Sanity check whilst debugging
          if (item.Id == 0
            || item.CreatedAt == DateTimeOffset.MinValue) {
            // Ruh roh
            Debugger.Break();
          }
#endif
        }

        // This conversion handles the restriction field and hash.
        var events = SharedMapper.Map<IEnumerable<IssueEventTableType>>(filteredEvents);

        // Set issueId
        foreach (var item in events) {
          item.IssueId = issueDetails.IssueId;
        }
        changes.UnionWith(await context.BulkUpdateIssueEvents(userId, issueDetails.RepositoryId, events, accountsParam.Select(x => x.Id)));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }
  }
}
