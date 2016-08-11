namespace RealArtists.ShipHub.QueueProcessor {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Threading.Tasks;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Microsoft.Azure;
  using Microsoft.Azure.WebJobs;
  using Newtonsoft.Json.Linq;
  using QueueClient;
  using QueueClient.Messages;
  using gm = Common.GitHub.Models;

  public static class SyncHandler {
    public static async Task AddOrUpdateRepoWebhooks(
      [ServiceBusTrigger(ShipHubQueueNames.AddOrUpdateRepoWebhooks)] AddOrUpdateRepoWebhooksMessage message) {
      await AddOrUpdateRepoWebhooksWithClient(message, GitHubSettings.CreateUserClient(message.AccessToken));
    }

    public static async Task AddOrUpdateRepoWebhooksWithClient(AddOrUpdateRepoWebhooksMessage message, IGitHubClient client) {
      using (var context = new ShipHubContext()) {
        var repo = await context.Repositories.SingleAsync(x => x.Id == message.RepositoryId);
        var requiredEvents = new string[] {
          "issues",
          //"issue_comment",
          //"member",
          //"public",
          //"pull_request",
          //"pull_request_review_comment",
          //"repository",
          //"team_add",
        };
        var apiHostname = CloudConfigurationManager.GetSetting("ApiHostname");
        if (apiHostname == null) {
          throw new ApplicationException("ApiHostname not specified in configuration.");
        }
        
        var hook = await context.Hooks.SingleOrDefaultAsync(x => x.RepositoryId == message.RepositoryId);

        if (hook == null) {
          var existingHooks = (await client.RepoWebhooks(repo.FullName)).Result
            .Where(x => x.Name.Equals("web"))
            .Where(x => x.Config.Url.StartsWith($"https://{apiHostname}/"));

          // Delete any existing hooks that already point back to us - don't
          // want to risk adding multiple Ship hooks.
          foreach (var existingHook in existingHooks) {
            var deleteResponse = await client.DeleteWebhook(repo.FullName, existingHook.Id);
            if (deleteResponse.IsError || !deleteResponse.Result) {
              Trace.TraceWarning($"Failed to delete existing hook ({existingHook.Id}) for repo '{repo.FullName}'");
            }
          }

          // GitHub will immediately send a ping when the webhook is created.
          // To avoid any chance for a race, add the Hook to the DB first, then
          // create on GitHub.
          hook = context.Hooks.Add(new Hook() {
            Active = false,
            Events = string.Join(",", requiredEvents),
            Secret = Guid.NewGuid(),
            RepositoryId = repo.Id,
          });
          await context.SaveChangesAsync();

          var addRepoHookResponse = await client.AddRepoWebhook(
            repo.FullName,
            new gm.Webhook() {
              Name = "web",
              Active = true,
              Events = requiredEvents,
              Config = new gm.WebhookConfiguration() {
                Url = $"https://{apiHostname}/webhook/repo/{repo.Id}",
                ContentType = "json",
                Secret = hook.Secret.ToString(),
              },
            });

          if (!addRepoHookResponse.IsError) {
            hook.GitHubId = addRepoHookResponse.Result.Id;
            await context.SaveChangesAsync();
          } else {
            Trace.TraceWarning($"Failed to add hook for repo '{repo.FullName}': {addRepoHookResponse.Error}");
            context.Hooks.Remove(hook);
            await context.SaveChangesAsync();
          }
        } else if (!new HashSet<string>(hook.Events.Split(',')).SetEquals(requiredEvents)) {
          var editResponse = await client.EditWebhookEvents(repo.FullName, hook.GitHubId, requiredEvents);

          if (editResponse.IsError) {
            Trace.TraceWarning($"Failed to edit hook for repo '{repo.FullName}': {editResponse.Error}");
          } else {            
            hook.Events = string.Join(",", editResponse.Result.Events);
            await context.SaveChangesAsync();
          }
        }
      }
    }

    public static async Task SyncAccount(
      [ServiceBusTrigger(ShipHubQueueNames.SyncAccount)] AccessTokenMessage message,
      [ServiceBus(ShipHubQueueNames.SyncAccountRepositories)] IAsyncCollector<AccountMessage> syncAccountRepos,
      [ServiceBus(ShipHubQueueNames.SyncAccountOrganizations)] IAsyncCollector<AccountMessage> syncAccountOrgs,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger) {
      using (var context = new ShipHubContext()) {
        var tasks = new List<Task>();
        ChangeSummary changes = null;

        var user = await context.Users.SingleAsync(x => x.Token == message.AccessToken);

        logger.WriteLine($"User details for {user.Login} cached until {user.Metadata?.Expires:o}");
        if (user.Metadata == null || user.Metadata.Expires < DateTimeOffset.UtcNow) {
          logger.WriteLine($"Polling: User");
          var ghc = GitHubSettings.CreateUserClient(user);
          var userResponse = await ghc.User(user.Metadata);

          if (userResponse.Status != HttpStatusCode.NotModified) {
            logger.WriteLine("GitHub: Changed. Saving changes.");
            changes = await context.UpdateAccount(
              userResponse.Date,
              SharedMapper.Map<AccountTableType>(userResponse.Result));
          } else {
            logger.WriteLine($"GitHub: Not modified.");
          }

          tasks.Add(context.UpdateMetaLimit("Accounts", user.Id, userResponse));
          tasks.Add(notifyChanges.Send(changes));
        } else {
          logger.WriteLine($"Waiting: Using cache from {user.OrganizationMetadata.LastRefresh:o}");
        }

        // Now that the user is saved in the DB, safe to sync all repos and user's orgs
        // These checks here cost nothing even though they're done inside the child tasks too.
        var am = new AccountMessage(user.Id, user.Login, message.AccessToken);

        logger.WriteLine($"Repositories cached until {user.RepositoryMetadata?.Expires:o}");
        if (user.RepositoryMetadata == null || user.RepositoryMetadata.Expires < DateTimeOffset.UtcNow) {
          logger.WriteLine("Updating account repositories.");
          tasks.Add(syncAccountRepos.AddAsync(am));
        } else {
          logger.WriteLine($"Skipping account repository update.");
        }

        logger.WriteLine($"Organizations cached until {user.OrganizationMetadata?.Expires:o}");
        if (user.OrganizationMetadata == null || user.OrganizationMetadata.Expires < DateTimeOffset.UtcNow) {
          logger.WriteLine("Updating account organizations.");
          tasks.Add(syncAccountOrgs.AddAsync(am));
        } else {
          logger.WriteLine($"Skipping account organizations update.");
        }

        await Task.WhenAll(tasks);
      }
    }

    public static async Task SyncAccountRepositories(
      [ServiceBusTrigger(ShipHubQueueNames.SyncAccountRepositories)] AccountMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepository)] IAsyncCollector<RepositoryMessage> syncRepo,
      [ServiceBus(ShipHubQueueNames.AddOrUpdateRepoWebhooks)] IAsyncCollector<AddOrUpdateRepoWebhooksMessage> addOrUpdateRepoWebhooks,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger) {
      using (var context = new ShipHubContext()) {
        var tasks = new List<Task>();
        ChangeSummary changes = null;

        var user = await context.Users.SingleAsync(x => x.Token == message.AccessToken);

        logger.WriteLine($"Account repositories for {user.Login} cached until {user.RepositoryMetadata?.Expires:o}");
        if (user.RepositoryMetadata == null || user.RepositoryMetadata.Expires < DateTimeOffset.UtcNow) {
          logger.WriteLine("Polling: Account repositories.");
          var ghc = GitHubSettings.CreateUserClient(user);
          var repoResponse = await ghc.Repositories(user.RepositoryMetadata);

          if (repoResponse.Status != HttpStatusCode.NotModified) {
            logger.WriteLine("Github: Changed. Saving changes.");
            var reposWithIssues = repoResponse.Result.Where(x => x.HasIssues);
            var assignableRepos = reposWithIssues.ToDictionary(x => x.FullName, x => ghc.IsAssignable(x.FullName, user.Login));
            await Task.WhenAll(assignableRepos.Values);
            var keepRepos = reposWithIssues.Where(x => assignableRepos[x.FullName].Result.Result).ToArray();

            var owners = keepRepos
              .Select(x => x.Owner)
              .GroupBy(x => x.Login)
              .Select(x => x.First());
            changes = await context.BulkUpdateAccounts(repoResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(owners));
            changes.UnionWith(await context.BulkUpdateRepositories(repoResponse.Date, SharedMapper.Map<IEnumerable<RepositoryTableType>>(keepRepos)));
            changes.UnionWith(await context.SetAccountLinkedRepositories(message.Id, keepRepos.Select(x => x.Id)));

            tasks.Add(notifyChanges.Send(changes));

            // Now that owners, repos, and links are saved, safe to sync the repos themselves.
            tasks.AddRange(keepRepos.Select(x => syncRepo.AddAsync(new RepositoryMessage(x.Id, x.FullName, user.Token))));

            // TODO: Do we want this here? May not trigger as often as Fred intended.
            tasks.AddRange(keepRepos
              .Where(x => x.Permissions.Admin)
              // Don't risk crapping over other people's repos yet.
              .Where(x => new string[] {
                "fpotter",
                "realartists",
                "realartists-test",
                "kogir",
                "james-howard",
                "aroon", // used in tests only
              }.Contains(x.Owner.Login))
              .Select(x => addOrUpdateRepoWebhooks.AddAsync(new AddOrUpdateRepoWebhooksMessage {
                RepositoryId = x.Id,
                AccessToken = message.AccessToken
              }))
            );
          } else {
            logger.WriteLine("Github: Not modified.");
          }

          tasks.Add(context.UpdateMetaLimit("Accounts", "RepoMetadataJson", user.Id, repoResponse));
        } else {
          logger.WriteLine($"Waiting: Using cache from {user.OrganizationMetadata.LastRefresh:o}");
        }

        await Task.WhenAll(tasks);
      }
    }

    /// NOTE WELL: We sync only sync orgs for which the user is a member. If they can see a repo in an org
    /// but aren't a member, too bad for them. The permissions are too painful otherwise.
    public static async Task SyncAccountOrganizations(
      [ServiceBusTrigger(ShipHubQueueNames.SyncAccountOrganizations)] AccountMessage message,
      [ServiceBus(ShipHubQueueNames.SyncOrganizationMembers)] IAsyncCollector<AccountMessage> syncOrgMembers,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger) {
      using (var context = new ShipHubContext()) {
        var tasks = new List<Task>();
        ChangeSummary changes = null;

        var user = await context.Users.SingleAsync(x => x.Id == message.Id);

        logger.WriteLine($"Organization memberships for {user.Login} cached until {user.OrganizationMetadata?.Expires}");
        if (user.OrganizationMetadata == null || user.OrganizationMetadata.Expires < DateTimeOffset.UtcNow) {
          logger.WriteLine("Polling: Organiztion membership.");
          var ghc = GitHubSettings.CreateUserClient(user);
          var orgResponse = await ghc.Organizations(user.OrganizationMetadata);

          if (orgResponse.Status != HttpStatusCode.NotModified) {
            logger.WriteLine("Github: Changed. Saving changes.");
            var orgs = orgResponse.Result;

            changes = await context.BulkUpdateAccounts(orgResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(orgs));
            changes.UnionWith(await context.SetUserOrganizations(message.Id, orgs.Select(x => x.Id)));

            tasks.AddRange(orgs.Select(x => syncOrgMembers.AddAsync(new AccountMessage(x.Id, x.Login, message.AccessToken))));
          } else {
            logger.WriteLine("Github: Not modified.");
          }

          // TODO: Even if the org memberships have not changed, do I want to refresh the orgs? Perhaps?

          tasks.Add(context.UpdateMetaLimit("Accounts", "OrgMetadataJson", user.Id, orgResponse));
        } else {
          logger.WriteLine($"Waiting: Using cache from {user.OrganizationMetadata.LastRefresh:o}");
        }

        tasks.Add(notifyChanges.Send(changes));
        await Task.WhenAll(tasks);
      }
    }

    public static async Task SyncOrganizationMembers(
      [ServiceBusTrigger(ShipHubQueueNames.SyncOrganizationMembers)] AccountMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      var memberResponse = await ghc.OrganizationMembers(message.Login);
      var members = memberResponse.Result;

      using (var context = new ShipHubContext()) {
        changes = await context.BulkUpdateAccounts(memberResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(members));
        changes.UnionWith(await context.SetOrganizationUsers(message.Id, members.Select(x => x.Id)));
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

      var assigneeResponse = await ghc.Assignable(message.FullName);
      var assignees = assigneeResponse.Result;

      using (var context = new ShipHubContext()) {
        changes = await context.BulkUpdateAccounts(assigneeResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(assignees));
        changes.UnionWith(await context.SetRepositoryAssignableAccounts(message.Id, assignees.Select(x => x.Id)));
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

      var milestoneResponse = await ghc.Milestones(message.FullName);
      var milestones = milestoneResponse.Result;

      using (var context = new ShipHubContext()) {
        changes = await context.BulkUpdateMilestones(message.Id, SharedMapper.Map<IEnumerable<MilestoneTableType>>(milestones));
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

      var labelResponse = await ghc.Labels(message.FullName);
      var labels = labelResponse.Result;

      using (var context = new ShipHubContext()) {
        changes = await context.SetRepositoryLabels(
          message.Id,
          labels.Select(x => new LabelTableType() {
            ItemId = message.Id,
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

      var issueResponse = await ghc.Issues(message.FullName);
      var issues = issueResponse.Result;

      using (var context = new ShipHubContext()) {
        var accounts = issues
          .SelectMany(x => new[] { x.User, x.ClosedBy }.Concat(x.Assignees))
          .Where(x => x != null)
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(issueResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(accounts));

        var milestones = issues
          .Select(x => x.Milestone)
          .Where(x => x != null)
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        changes.UnionWith(await context.BulkUpdateMilestones(message.Id, SharedMapper.Map<IEnumerable<MilestoneTableType>>(milestones)));

        changes.UnionWith(await context.BulkUpdateIssues(
          message.Id,
          SharedMapper.Map<IEnumerable<IssueTableType>>(issues),
          issues.SelectMany(x => x.Labels?.Select(y => new LabelTableType() { ItemId = x.Id, Color = y.Color, Name = y.Name })),
          issues.SelectMany(x => x.Assignees?.Select(y => new MappingTableType() { Item1 = x.Id, Item2 = y.Id }))
        ));
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

      var commentsResponse = await ghc.Comments(message.FullName);
      var comments = commentsResponse.Result;

      using (var context = new ShipHubContext()) {
        var users = comments
          .Select(x => x.User)
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(commentsResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(users));

        var issueComments = comments.Where(x => x.IssueNumber != null);
        changes.UnionWith(await context.BulkUpdateComments(message.Id, SharedMapper.Map<IEnumerable<CommentTableType>>(issueComments), complete: true));
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

      var eventsResponse = await ghc.Events(message.FullName);
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
        changes.UnionWith(await context.BulkUpdateIssueEvents(userId, message.Id, eventsParam, accountsParam.Select(x => x.Id)));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    public static async Task SyncRepositoryIssueComments(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueComments)] IssueMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueCommentReactions)] IAsyncCollector<IssueCommentMessage> syncReactions,
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

      var tasks = comments.Select(x => syncReactions.AddAsync(new IssueCommentMessage(x.Id, message.AccessToken))).ToList();

      if (!changes.Empty) {
        tasks.Add(notifyChanges.AddAsync(new ChangeMessage(changes)));
      }

      await Task.WhenAll(tasks);
    }

    public static async Task SyncRepositoryIssueReactions(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueReactions)] IssueMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      using (var context = new ShipHubContext()) {
        // TODO: Optimize queue messages to remove/reduce these lookups
        var details = await context.Issues
          .Where(x => x.Number == message.Number && x.Repository.FullName == message.RepositoryFullName)
          .Select(x => new {
            IssueId = x.Id,
            IssueNumber = x.Number,
            RepositoryId = x.RepositoryId,
            RepoFullName = x.Repository.FullName,
          })
          .SingleAsync();

        var reactionsResponse = await ghc.IssueReactions(details.RepoFullName, details.IssueNumber);
        var reactions = reactionsResponse.Result;

        var users = reactions
          .Select(x => x.User)
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(reactionsResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(users));

        changes.UnionWith(await context.BulkUpdateIssueReactions(
          details.RepositoryId,
          details.IssueId,
          SharedMapper.Map<IEnumerable<ReactionTableType>>(reactions)));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    public static async Task SyncRepositoryIssueCommentReactions(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueCommentReactions)] IssueCommentMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges) {
      var ghc = GitHubSettings.CreateUserClient(message.AccessToken);
      ChangeSummary changes;

      using (var context = new ShipHubContext()) {
        // TODO: Optimize queue messages to remove/reduce these lookups
        var details = await context.Comments
          .Where(x => x.Id == message.CommentId)
          .Select(x => new {
            CommentId = x.Id,
            IssueId = x.IssueId,
            RepositoryId = x.RepositoryId,
            RepoFullName = x.Repository.FullName,
          })
          .SingleAsync();

        var reactionsResponse = await ghc.IssueCommentReactions(details.RepoFullName, details.CommentId);
        var reactions = reactionsResponse.Result;

        var users = reactions
          .Select(x => x.User)
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        changes = await context.BulkUpdateAccounts(reactionsResponse.Date, SharedMapper.Map<IEnumerable<AccountTableType>>(users));

        changes.UnionWith(await context.BulkUpdateCommentReactions(
          details.RepositoryId,
          details.CommentId,
          SharedMapper.Map<IEnumerable<ReactionTableType>>(reactions)));
      }

      if (!changes.Empty) {
        await notifyChanges.AddAsync(new ChangeMessage(changes));
      }
    }

    private static HashSet<string> _FilterEvents = new HashSet<string>(new[] { "commented", "subscribed", "unsubscribed" }, StringComparer.OrdinalIgnoreCase);
    public static async Task SyncRepositoryIssueTimeline(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueTimeline)] IssueMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueComments)] IAsyncCollector<IssueMessage> syncIssueComments,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueReactions)] IAsyncCollector<IssueMessage> syncRepoIssueReactions,
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

      // TODO: Just trigger a full repo issue sync once incremental is implemented
      var issueResponse = await ghc.Issue(message.RepositoryFullName, message.Number);
      var issue = issueResponse.Result;

      var timelineResponse = await ghc.Timeline(message.RepositoryFullName, message.Number);
      var timeline = timelineResponse.Result;

      // Now just filter
      var filteredEvents = timeline.Where(x => !_FilterEvents.Contains(x.Event)).ToArray();

      using (var context = new ShipHubContext()) {
        // TODO: Gross
        var userId = await context.Users
          .Where(x => x.Token == message.AccessToken)
          .Select(x => x.Id)
          .SingleAsync();

        // HACK: This is broken. Will fail when the issue is not yet saved to the DB.
        var issueDetails = await context.Issues
          .Where(x => x.Repository.FullName == message.RepositoryFullName)
          .Where(x => x.Number == message.Number)
          .Select(x => new { IssueId = x.Id, RepositoryId = x.RepositoryId })
          .SingleAsync();

        // For adding to the DB later
        var accounts = new List<gm.Account>();
        accounts.Add(issue.ClosedBy);
        accounts.Add(issue.User);
        if (issue.Assignees.Any()) {
          accounts.AddRange(issue.Assignees);
        }

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
            var lookup = commitLookups[item.CommitUrl].Result;

            // best effort - requests will fail when the user doesn't have source access.
            // see Nick's account and references from the github-beta repo
            if (lookup.IsError) {
              continue;
            }

            var commit = lookup.Result;
            accounts.Add(commit.Author);
            accounts.Add(commit.Committer);
            item.ExtensionDataDictionary["ship_commit_message"] = commit.CommitDetails.Message;
            item.ExtensionDataDictionary["ship_commit_author"] = JObject.FromObject(commit.Author);
            item.ExtensionDataDictionary["ship_commit_committer"] = JObject.FromObject(commit.Committer);
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
              var url = x.Result.Result.PullRequest.Url;
              var parts = url.Split('/');
              var numParts = parts.Length;
              var repoName = parts[numParts - 4] + "/" + parts[numParts - 3];
              var prNum = int.Parse(parts[numParts - 1]);
              return new {
                Id = url,
                Task = ghc.PullRequest(repoName, prNum),
              };
            })
            .ToDictionary(x => x.Id, x => x.Task);

          await Task.WhenAll(prLookups.Values);

          foreach (var item in withSources) {
            var refIssue = sourceLookups[item.Source.IssueUrl].Result.Result;
            accounts.Add(item.Source.Actor);
            if (refIssue.Assignees.Any()) {
              accounts.AddRange(refIssue.Assignees); // Do we need both assignee and assignees? I think yes.
            }
            accounts.Add(refIssue.ClosedBy);
            accounts.Add(refIssue.User);

            item.ExtensionDataDictionary["ship_issue_state"] = refIssue.State;
            item.ExtensionDataDictionary["ship_issue_title"] = refIssue.Title;

            if (refIssue.PullRequest != null) {
              item.ExtensionDataDictionary["ship_is_pull_request"] = true;

              var pr = prLookups[refIssue.PullRequest.Url].Result.Result;
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

        // Update issue
        changes.UnionWith(await context.BulkUpdateIssues(
          issueDetails.RepositoryId,
          new[] { SharedMapper.Map<IssueTableType>(issue) },
          issue.Labels?.Select(x => new LabelTableType() {
            ItemId = issue.Id,
            Color = x.Color,
            Name = x.Name,
          }),
          issue.Assignees?.Select(x => new MappingTableType() {
            Item1 = issue.Id,
            Item2 = x.Id,
          })));

        // Now safe to sync reactions
        tasks.Add(syncRepoIssueReactions.AddAsync(message));

        // If we find comments, sync them
        // TODO: Incrementally
        if (timeline.Any(x => x.Event == "commented")) {
          tasks.Add(syncIssueComments.AddAsync(message));
          // Can't sync comment reactions yet in case they don't exist
        }

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
        changes.UnionWith(await context.BulkUpdateTimelineEvents(userId, issueDetails.RepositoryId, events, accountsParam.Select(x => x.Id)));
      }

      if (!changes.Empty) {
        tasks.Add(notifyChanges.AddAsync(new ChangeMessage(changes)));
      }

      await Task.WhenAll(tasks);
    }
  }

  public static class SyncHandlerExtensions {
    public static Task Send(this IAsyncCollector<ChangeMessage> topic, ChangeSummary summary) {
      if (summary != null && !summary.Empty) {
        return topic.AddAsync(new ChangeMessage(summary));
      }
      return Task.CompletedTask;
    }
  }
}
