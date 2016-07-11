namespace RealArtists.ShipHub.QueueProcessor {
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Microsoft.Azure.WebJobs;
  using QueueClient;
  using QueueClient.Messages;

  /// <summary>
  /// TODO: ENSURE PARTITIONING AND MESSAGE IDS ARE SET CORRECTLY.
  /// 
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
      var assignableRepos = reposWithIssues.ToDictionary(x => x.FullName, x => ghc.Assignable(x.FullName, message.Account.Login));
      await Task.WhenAll(assignableRepos.Values.ToArray());
      var keepRepos = reposWithIssues.Where(x => assignableRepos[x.FullName].Result.Result).ToArray();

      using (var context = new ShipHubContext()) {
        var owners = keepRepos
          .Select(x => x.Owner)
          .GroupBy(x => x.Login)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(repoResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(owners));
        changes.UnionWith(await context.BulkUpdateRepositories(repoResponse.Date, SharedMapper.Map<IEnumerable<RepositoryTableType>>(keepRepos)));
        await context.SetAccountLinkedRepositories(message.Account.Id, keepRepos.Select(x => x.Id));
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
        await context.SetUserOrganizations(message.Account.Id, orgs.Select(x => x.Id));
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
      [ServiceBus(ShipHubQueueNames.SyncRepositoryLabels)] IAsyncCollector<RepositoryMessage> syncRepoLabels,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueEvents)] IAsyncCollector<RepositoryMessage> syncRepoIssueEvents) {
      // This is just a fanout point.
      // Plan to add conditional checks here to reduce polling frequency.
      await Task.WhenAll(
        syncRepoAssignees.AddAsync(message),
        syncRepoMilestones.AddAsync(message),
        syncRepoLabels.AddAsync(message),
        syncRepoIssueEvents.AddAsync(message) // This only works now because we don't parse any event details.
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
            Id = message.Repository.Id,
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
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var issueResponse = await ghc.Issues(message.Repository.FullName);
      var issues = issueResponse.Result
        .Where(x => x.PullRequest == null); // Drop pull requests for now

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
          issues.SelectMany(x => x.Labels.Select(y => new LabelTableType() { Id = x.Id, Color = y.Color, Name = y.Name }))));
      }

      await Task.WhenAll(
        changes.Empty ? Task.CompletedTask : notifyChanges.AddAsync(new ChangeMessage(changes)),
        syncRepoComments.AddAsync(message));
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
        changes.UnionWith(await context.BulkUpdateComments(message.Repository.Id, SharedMapper.Map<IEnumerable<CommentTableType>>(issueComments)));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    //public static async Task SyncRepositoryIssueEvents(
    //  [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueEvents)] RepositoryMessage message,
    //  [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
    //  var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
    //  ChangeSummary changes;

    //  var eventsResponse = await ghc.Events(message.Repository.FullName);
    //  var events = eventsResponse.Result;

    //  using (var context = new ShipHubContext()) {
    //    // For now only grab accounts from the response.
    //    // Sometimes an issue is also included, but not always, and we get them elsewhere anyway.
    //    var accounts = events
    //      .SelectMany(x => new[] { x.Actor, x.Assignee, x.Assigner })
    //      .Where(x => x != null)
    //      .GroupBy(x => x.Login)
    //      .Select(x => x.First());
    //    changes = await context.BulkUpdateAccounts(eventsResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(accounts));
    //    changes.UnionWith(await context.BulkUpdateIssueEvents(message.Repository.Id, SharedMapper.Map<IEnumerable<IssueEventTableType>>(events)));
    //  }

    //  if (!changes.Empty) {
    //    await notifyChanges.AddAsync(new ChangeMessage(changes));
    //  }
    //}

    public static async Task SyncRepositoryIssueTimeline(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueTimeline)] IssueMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var timelineResponse = await ghc.Timeline(message.RepositoryFullName, message.Number);
      var timeline = timelineResponse.Result;

      using (var context = new ShipHubContext()) {
        // TODO: I don't like this
        var issueDetails = await context.Issues
          .Where(x => x.Repository.FullName == message.RepositoryFullName)
          .Where(x => x.Number == message.Number)
          .Select(x => new { IssueId = x.Id, RepositoryId = x.RepositoryId })
          .SingleAsync();
        // For now only grab accounts from the response.
        // Sometimes an issue is also included, but not always, and we get them elsewhere anyway.
        var accounts = timeline
          .SelectMany(x => new[] { x.Actor, x.Assignee })
          .Where(x => x != null)
          .GroupBy(x => x.Login)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(timelineResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(accounts));
        var events = SharedMapper.Map<IEnumerable<IssueEventTableType>>(timeline);
        foreach (var item in events) {
          item.IssueId = issueDetails.IssueId;
        }
        changes.UnionWith(await context.BulkUpdateIssueEvents(issueDetails.RepositoryId, events));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }
  }
}
