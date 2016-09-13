namespace RealArtists.ShipHub.QueueProcessor.Jobs {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Threading.Tasks;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Microsoft.Azure.WebJobs;
  using Microsoft.ServiceBus.Messaging;
  using Newtonsoft.Json.Linq;
  using QueueClient;
  using QueueClient.Messages;
  using Tracing;
  using gm = Common.GitHub.Models;

  public class SyncQueueHandler : LoggingHandlerBase {
    private IMapper _mapper;

    public SyncQueueHandler(IDetailedExceptionLogger logger, IMapper mapper) : base(logger) {
      _mapper = mapper;
    }

    public async Task SyncAccount(
      [ServiceBusTrigger(ShipHubQueueNames.SyncAccount)] UserIdMessage message,
      [ServiceBus(ShipHubQueueNames.SyncAccountRepositories)] IAsyncCollector<UserIdMessage> syncAccountRepos,
      [ServiceBus(ShipHubQueueNames.SyncAccountOrganizations)] IAsyncCollector<UserIdMessage> syncAccountOrgs,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.UserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.UserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          logger.WriteLine($"User details for {user.Login} cached until {user.Metadata?.Expires:o}");
          if (user.Metadata == null || user.Metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine($"Polling: User");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());
            var userResponse = await ghc.User(user.Metadata.IfValidFor(user));

            if (userResponse.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("GitHub: Changed. Saving changes.");
              changes = await context.UpdateAccount(
                userResponse.Date,
                _mapper.Map<AccountTableType>(userResponse.Result));
            } else {
              logger.WriteLine($"GitHub: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Accounts", user.Id, userResponse));
            tasks.Add(notifyChanges.Send(changes));
          } else {
            logger.WriteLine($"Waiting: Using cache from {user.Metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);

          // Now that the user is saved in the DB, safe to sync all repos and user's orgs
          var am = new UserIdMessage(user.Id);
          tasks.Add(syncAccountRepos.AddAsync(am));
          tasks.Add(syncAccountOrgs.AddAsync(am));

          await Task.WhenAll(tasks);
        }
      });
    }

    public async Task SyncAccountRepositories(
      [ServiceBusTrigger(ShipHubQueueNames.SyncAccountRepositories)] UserIdMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepository)] IAsyncCollector<TargetMessage> syncRepo,
      [ServiceBus(ShipHubQueueNames.AddOrUpdateRepoWebhooks)] IAsyncCollector<BrokeredMessage> addOrUpdateRepoWebhooks,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.UserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.UserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          logger.WriteLine($"Account repositories for {user.Login} cached until {user.RepositoryMetadata?.Expires:o}");
          if (user.RepositoryMetadata == null || user.RepositoryMetadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Account repositories.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());
            var repoResponse = await ghc.Repositories(user.RepositoryMetadata.IfValidFor(user));

            if (repoResponse.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var reposWithIssues = repoResponse.Result.Where(x => x.HasIssues);
              var assignableRepos = reposWithIssues.ToDictionary(x => x.FullName, x => ghc.IsAssignable(x.FullName, user.Login));
              await Task.WhenAll(assignableRepos.Values);
              var keepRepos = reposWithIssues.Where(x => assignableRepos[x.FullName].Result.Result).ToArray();

              var owners = keepRepos
                .Select(x => x.Owner)
                .Distinct(x => x.Login);
              changes = await context.BulkUpdateAccounts(repoResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(owners));
              changes.UnionWith(await context.BulkUpdateRepositories(repoResponse.Date, _mapper.Map<IEnumerable<RepositoryTableType>>(keepRepos)));
              changes.UnionWith(await context.SetAccountLinkedRepositories(user.Id, keepRepos.Select(x => Tuple.Create(x.Id, x.Permissions.Admin))));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Accounts", "RepoMetadataJson", user.Id, repoResponse));
          } else {
            logger.WriteLine($"Waiting: Using cache from {user.OrganizationMetadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);

          var repos = await context.AccountRepositories
            .Where(x => x.AccountId == user.Id)
            .ToArrayAsync();

          // Now that owners, repos, and links are saved, safe to sync the repos themselves.
          await Task.WhenAll(repos.Select(x => syncRepo.AddAsync(new TargetMessage(x.RepositoryId, user.Id))));
          await Task.WhenAll(repos
            .Where(x => x.Admin)
            .Select(x => addOrUpdateRepoWebhooks.AddAsync(WebJobInterop.CreateMessage(new TargetMessage(x.RepositoryId, user.Id), $"repo-{x.RepositoryId}"))));
        }
      });
    }

    /// NOTE WELL: We sync only sync orgs for which the user is a member. If they can see a repo in an org
    /// but aren't a member, too bad for them. The permissions are too painful otherwise.
    public async Task SyncAccountOrganizations(
      [ServiceBusTrigger(ShipHubQueueNames.SyncAccountOrganizations)] UserIdMessage message,
      [ServiceBus(ShipHubQueueNames.SyncOrganizationMembers)] IAsyncCollector<TargetMessage> syncOrgMembers,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.UserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.UserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          logger.WriteLine($"Organization memberships for {user.Login} cached until {user.OrganizationMetadata?.Expires}");
          if (user.OrganizationMetadata == null || user.OrganizationMetadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Organization membership.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());
            var orgResponse = await ghc.OrganizationMemberships(user.OrganizationMetadata.IfValidFor(user));

            if (orgResponse.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var orgs = orgResponse.Result;

              changes = await context.BulkUpdateAccounts(orgResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(orgs.Select(x => x.Organization)));
              changes.UnionWith(await context.SetUserOrganizations(user.Id, orgs.Select(x => x.Organization.Id)));
              tasks.Add(notifyChanges.Send(changes));
            } else {
              // TODO: Even if the org memberships have not changed, do I want to refresh the orgs? Perhaps?
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Accounts", "OrgMetadataJson", user.Id, orgResponse));
          } else {
            logger.WriteLine($"Waiting: Using cache from {user.OrganizationMetadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);

          var allOrgIds = await context.OrganizationAccounts
            .Where(x => x.UserId == user.Id)
            .Select(x => x.OrganizationId)
            .ToArrayAsync();

          // Refresh member list as well
          if (allOrgIds.Any()) {
            await Task.WhenAll(allOrgIds.Select(x => syncOrgMembers.AddAsync(new TargetMessage(x, user.Id))));
          }
        }
      });
    }

    public async Task SyncOrganizationMembers(
      [ServiceBusTrigger(ShipHubQueueNames.SyncOrganizationMembers)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      [ServiceBus(ShipHubQueueNames.AddOrUpdateOrgWebhooks)] IAsyncCollector<BrokeredMessage> addOrUpdateOrgWebhooks,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var org = await context.Organizations.SingleAsync(x => x.Id == message.TargetId);

          logger.WriteLine($"Organization members for {org.Login} cached until {org.OrganizationMetadata?.Expires}");
          if (org.OrganizationMetadata == null || org.OrganizationMetadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Organization membership.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            // GitHub's `/orgs/<name>/members` endpoint does not provide role info for
            // each member.  To workaround, we make two requests and use the filter option
            // to only get admins or non-admins on each request.
            var membersTask = ghc.OrganizationMembers(org.Login, role: "member", cacheOptions: GitHubCacheDetails.Empty);
            var adminsTask = ghc.OrganizationMembers(org.Login, role: "admin", cacheOptions: GitHubCacheDetails.Empty);
            await Task.WhenAll(membersTask, adminsTask);

            var members = membersTask.Result.Result;
            var admins = adminsTask.Result.Result;

            changes = await context.BulkUpdateAccounts(
              membersTask.Result.Date,
              _mapper.Map<IEnumerable<AccountTableType>>(members.Concat(admins)));
            changes.UnionWith(await context.SetOrganizationUsers(
              org.Id,
              members.Select(x => Tuple.Create(x.Id, false))
              .Concat(admins.Select(x => Tuple.Create(x.Id, true)))));

            tasks.Add(notifyChanges.Send(changes));
            tasks.Add(context.UpdateMetadata("Accounts", "OrgMetadataJson", org.Id, membersTask.Result));
          } else {
            logger.WriteLine($"Waiting: Using cache from {user.OrganizationMetadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);

          var membership = await context.OrganizationAccounts
            .SingleOrDefaultAsync(x => x.OrganizationId == message.TargetId && x.UserId == message.ForUserId);
          if (membership != null && membership.Admin) {
            await addOrUpdateOrgWebhooks.AddAsync(WebJobInterop.CreateMessage(message, $"org-{message.TargetId}"));
          }
        }
      });
    }

    /// <summary>
    /// Precondition: Repos saved in DB
    /// Postcondition: None.
    /// </summary>
    /// TODO: Should this be inlined?
    public async Task SyncRepository(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepository)] TargetMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryAssignees)] IAsyncCollector<TargetMessage> syncRepoAssignees,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryMilestones)] IAsyncCollector<TargetMessage> syncRepoMilestones,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryLabels)] IAsyncCollector<TargetMessage> syncRepoLabels,
      ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        // TODO: Refresh the repository itself.

        await Task.WhenAll(
          syncRepoAssignees.AddAsync(message),
          syncRepoMilestones.AddAsync(message),
          syncRepoLabels.AddAsync(message)
        );
      });
    }

    /// <summary>
    /// Precondition: Repository exists
    /// Postcondition: Repository assignees exist and are linked.
    /// </summary>
    public async Task SyncRepositoryAssignees(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryAssignees)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var repo = await context.Repositories.SingleAsync(x => x.Id == message.TargetId);
          var metadata = repo.AssignableMetadata;

          logger.WriteLine($"Assignees for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository assignees.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            var response = await ghc.Assignable(repo.FullName, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var assignees = response.Result;

              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(assignees));
              changes.UnionWith(await context.SetRepositoryAssignableAccounts(repo.Id, assignees.Select(x => x.Id)));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "AssignableMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    /// <summary>
    /// Precondition: Repository exists
    /// Postcondition: Milestones exist
    /// </summary>
    public async Task SyncRepositoryMilestones(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryMilestones)] TargetMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssues)] IAsyncCollector<TargetMessage> syncRepoIssues,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var repo = await context.Repositories.SingleAsync(x => x.Id == message.TargetId);
          var metadata = repo.MilestoneMetadata;

          logger.WriteLine($"Milestones for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository milestones.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            var response = await ghc.Milestones(repo.FullName, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var milestones = response.Result;

              changes = await context.BulkUpdateMilestones(repo.Id, _mapper.Map<IEnumerable<MilestoneTableType>>(milestones));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "MilestoneMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);

          // Sync Issues regardless
          await syncRepoIssues.AddAsync(message);
        }
      });
    }

    /// <summary>
    /// Precondition: Repository exists
    /// Postcondition: Labels exist
    /// </summary>
    public async Task SyncRepositoryLabels(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryLabels)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var repo = await context.Repositories.SingleAsync(x => x.Id == message.TargetId);
          var metadata = repo.LabelMetadata;

          logger.WriteLine($"Labels for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository labels.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            var response = await ghc.Labels(repo.FullName, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var labels = response.Result;

              changes = await context.SetRepositoryLabels(
                repo.Id,
                labels.Select(x => new LabelTableType() {
                  ItemId = repo.Id,
                  Color = x.Color,
                  Name = x.Name
                })
              );

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "LabelMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    /// <summary>
    /// Precondition: Repository and Milestones exist
    /// Postcondition: Issues exist
    /// </summary>
    public async Task SyncRepositoryIssues(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssues)] TargetMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryComments)] IAsyncCollector<TargetMessage> syncRepoComments,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueEvents)] IAsyncCollector<TargetMessage> syncRepoIssueEvents,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var repo = await context.Repositories.SingleAsync(x => x.Id == message.TargetId);
          var metadata = repo.IssueMetadata;

          logger.WriteLine($"Issues for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository issues.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            var response = await ghc.Issues(repo.FullName, null, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var issues = response.Result;

              var accounts = issues
                .SelectMany(x => new[] { x.User, x.ClosedBy }.Concat(x.Assignees))
                .Where(x => x != null)
                .Distinct(x => x.Id);
              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(accounts));

              var milestones = issues
                .Select(x => x.Milestone)
                .Where(x => x != null)
                .Distinct(x => x.Id);
              changes.UnionWith(await context.BulkUpdateMilestones(repo.Id, _mapper.Map<IEnumerable<MilestoneTableType>>(milestones)));

              changes.UnionWith(await context.BulkUpdateIssues(
                repo.Id,
                _mapper.Map<IEnumerable<IssueTableType>>(issues),
                issues.SelectMany(x => x.Labels?.Select(y => new LabelTableType() { ItemId = x.Id, Color = y.Color, Name = y.Name })),
                issues.SelectMany(x => x.Assignees?.Select(y => new MappingTableType() { Item1 = x.Id, Item2 = y.Id }))
              ));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "IssueMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);

          // Do these unconditionally
          await Task.WhenAll(
            syncRepoComments.AddAsync(message),
            syncRepoIssueEvents.AddAsync(message)
          );
        }
      });
    }

    public async Task SyncRepositoryComments(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryComments)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var repo = await context.Repositories.SingleAsync(x => x.Id == message.TargetId);
          var metadata = repo.CommentMetadata;

          logger.WriteLine($"Comments for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository comments.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            var response = await ghc.Comments(repo.FullName, null, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var comments = response.Result;

              var users = comments
                .Select(x => x.User)
                .Distinct(x => x.Id);
              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(users));

              var issueComments = comments.Where(x => x.IssueNumber != null);
              changes.UnionWith(await context.BulkUpdateComments(repo.Id, _mapper.Map<IEnumerable<CommentTableType>>(issueComments), complete: true));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "CommentMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    public async Task SyncRepositoryIssueEvents(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueEvents)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var repo = await context.Repositories.SingleAsync(x => x.Id == message.TargetId);
          var metadata = repo.EventMetadata;

          logger.WriteLine($"Events for {repo.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Repository events.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            // TODO: Cute pagination trick to detect latest only.
            var response = await ghc.Events(repo.FullName, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var events = response.Result;

              // For now only grab accounts from the response.
              // Sometimes an issue is also included, but not always, and we get them elsewhere anyway.
              var accounts = events
                .SelectMany(x => new[] { x.Actor, x.Assignee, x.Assigner })
                .Where(x => x != null)
                .Distinct(x => x.Login);
              var accountsParam = _mapper.Map<IEnumerable<AccountTableType>>(accounts);
              changes = await context.BulkUpdateAccounts(response.Date, accountsParam);
              var eventsParam = _mapper.Map<IEnumerable<IssueEventTableType>>(events);
              changes.UnionWith(await context.BulkUpdateIssueEvents(user.Id, repo.Id, eventsParam, accountsParam.Select(x => x.Id)));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Repositories", "EventMetadataJson", repo.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    public async Task SyncRepositoryIssueComments(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueComments)] TargetMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueCommentReactions)] IAsyncCollector<TargetMessage> syncReactions,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var issue = await context.Issues
            .Include(x => x.Repository)
            .SingleAsync(x => x.Id == message.TargetId);
          var metadata = issue.CommentMetadata;

          logger.WriteLine($"Comments for {issue.Repository.FullName}#{issue.Number} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Issue comments.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            // TODO: Cute pagination trick to detect latest only.
            var response = await ghc.Comments(issue.Repository.FullName, null, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var comments = response.Result;

              var users = comments
                .Select(x => x.User)
                .Distinct(x => x.Id);
              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(users));

              changes.UnionWith(await context.BulkUpdateIssueComments(
                issue.Repository.FullName,
                issue.Number,
                _mapper.Map<IEnumerable<CommentTableType>>(comments),
                complete: true));

              tasks.Add(notifyChanges.Send(changes));

              tasks.AddRange(comments.Select(x => syncReactions.AddAsync(new TargetMessage(x.Id, message.ForUserId))));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Issues", "CommentMetadataJson", issue.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    public async Task SyncRepositoryIssueReactions(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueReactions)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var issue = await context.Issues
            .Include(x => x.Repository)
            .SingleAsync(x => x.Id == message.TargetId);
          var metadata = issue.ReactionMetadata;

          logger.WriteLine($"Reactions for {issue.Repository.FullName}#{issue.Number} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Issue reactions.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            var response = await ghc.IssueReactions(issue.Repository.FullName, issue.Number, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var reactions = response.Result;

              var users = reactions
                .Select(x => x.User)
                .Distinct(x => x.Id);
              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(users));

              changes.UnionWith(await context.BulkUpdateIssueReactions(
                issue.RepositoryId,
                issue.Id,
                _mapper.Map<IEnumerable<ReactionTableType>>(reactions)));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Issues", "ReactionMetadataJson", issue.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    public async Task SyncRepositoryIssueCommentReactions(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueCommentReactions)] TargetMessage message,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = null;

          // Lookup requesting user and org.
          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var comment = await context.Comments
            .Include(x => x.Repository)
            .SingleAsync(x => x.Id == message.TargetId);
          var metadata = comment.ReactionMetadata;

          logger.WriteLine($"Reactions for comment {comment.Id} in {comment.Repository.FullName} cached until {metadata?.Expires}");
          if (metadata == null || metadata.Expires < DateTimeOffset.UtcNow) {
            logger.WriteLine("Polling: Issue reactions.");
            var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

            var response = await ghc.IssueCommentReactions(comment.Repository.FullName, comment.Id, metadata);
            if (response.Status != HttpStatusCode.NotModified) {
              logger.WriteLine("Github: Changed. Saving changes.");
              var reactions = response.Result;

              var users = reactions
                .Select(x => x.User)
                .Distinct(x => x.Id);
              changes = await context.BulkUpdateAccounts(response.Date, _mapper.Map<IEnumerable<AccountTableType>>(users));

              changes.UnionWith(await context.BulkUpdateCommentReactions(
                comment.RepositoryId,
                comment.Id,
                _mapper.Map<IEnumerable<ReactionTableType>>(reactions)));

              tasks.Add(notifyChanges.Send(changes));
            } else {
              logger.WriteLine("Github: Not modified.");
            }

            tasks.Add(context.UpdateMetadata("Comments", "ReactionMetadataJson", comment.Id, response));
          } else {
            logger.WriteLine($"Waiting: Using cache from {metadata.LastRefresh:o}");
          }

          await Task.WhenAll(tasks);
        }
      });
    }

    private static HashSet<string> _IgnoreTimelineEvents = new HashSet<string>(new[] { "commented", "subscribed", "unsubscribed" }, StringComparer.OrdinalIgnoreCase);
    public async Task SyncRepositoryIssueTimeline(
      [ServiceBusTrigger(ShipHubQueueNames.SyncRepositoryIssueTimeline)] IssueViewMessage message,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueComments)] IAsyncCollector<TargetMessage> syncIssueComments,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssueReactions)] IAsyncCollector<TargetMessage> syncRepoIssueReactions,
      [ServiceBus(ShipHubQueueNames.SyncRepositoryIssues)] IAsyncCollector<TargetMessage> syncRepoIssues,
      [ServiceBus(ShipHubTopicNames.Changes)] IAsyncCollector<ChangeMessage> notifyChanges,
      TextWriter logger, ExecutionContext executionContext) {
      ///////////////////////////////////////////
      /* NOTE!
       * We can't sync the timeline incrementally, because the client wants commit and
       * reference data inlined. This means we always have to download all the
       * timeline events in case an old one now has updated data. Other options are to
       * just be wrong, or to simply reference the user by id and mark them referenced
       * by the repo.
       */
      //////////////////////////////////////////
      await WithEnhancedLogging(executionContext.InvocationId, message.ForUserId, message, async () => {
        using (var context = new ShipHubContext()) {
          var tasks = new List<Task>();
          ChangeSummary changes = new ChangeSummary();

          var user = await context.Users.SingleOrDefaultAsync(x => x.Id == message.ForUserId);
          if (user == null || user.Token.IsNullOrWhiteSpace()) {
            return;
          }

          var ghc = GitHubSettings.CreateUserClient(user, executionContext.InvocationId.ToString());

          // Client doesn't send repoId :(
          var repoId = await context.Repositories
            .Where(x => x.FullName == message.RepositoryFullName)
            .Select(x => x.Id)
            .SingleAsync();

          // Look up the issue info
          var issueInfo = await context.Issues
            .Where(x => x.RepositoryId == repoId && x.Number == message.Number)
            .SingleOrDefaultAsync();

          var issueId = issueInfo?.Id;

          // Sadly there exist cases where the client knows about issues before the server.
          // Even hooks aren't fast enough to completely prevent this.
          // The intercepting proxy should alleviate most but not all cases.
          // In the meantime, always discover/refresh the issue on view
          var issueResponse = await ghc.Issue(message.RepositoryFullName, message.Number, issueInfo?.Metadata);
          if (issueResponse.Status != HttpStatusCode.NotModified) {
            var update = issueResponse.Result;

            issueId = update.Id;

            // TODO: Unify this code with other issue update places to reduce bugs.

            var upAccounts = new[] { update.User, update.ClosedBy }.Concat(update.Assignees)
                .Where(x => x != null)
                .Distinct(x => x.Id);
            changes.UnionWith(await context.BulkUpdateAccounts(issueResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(upAccounts)));

            if (update.Milestone != null) {
              changes.UnionWith(await context.BulkUpdateMilestones(repoId, _mapper.Map<IEnumerable<MilestoneTableType>>(new[] { update.Milestone })));
            }

            changes.UnionWith(await context.BulkUpdateIssues(
              repoId,
              _mapper.Map<IEnumerable<IssueTableType>>(new[] { update }),
              update.Labels?.Select(y => new LabelTableType() { ItemId = update.Id, Color = y.Color, Name = y.Name }),
              update.Assignees?.Select(y => new MappingTableType() { Item1 = update.Id, Item2 = y.Id })
            ));
          }

          tasks.Add(context.UpdateMetadata("Issues", "MetadataJson", issueId.Value, issueResponse));

          // This will be cached per-user by the ShipHubFilter.
          var timelineResponse = await ghc.Timeline(message.RepositoryFullName, message.Number);
          if (timelineResponse.Status != HttpStatusCode.NotModified) {
            var timeline = timelineResponse.Result;

            // Now just filter
            var filteredEvents = timeline.Where(x => !_IgnoreTimelineEvents.Contains(x.Event)).ToArray();

            // For adding to the DB later
            var accounts = new List<gm.Account>();

            foreach (var tl in filteredEvents) {
              accounts.Add(tl.Actor);
              accounts.Add(tl.Assignee);
              accounts.Add(tl.Source?.Actor);
            }

            // Find all events with associated commits, and embed them.
            var withCommits = filteredEvents.Where(x => !x.CommitUrl.IsNullOrWhiteSpace()).ToArray();
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
                    Task = ghc.Commit(repoName, sha, GitHubCacheDetails.Empty),
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
                if (commit.Author != null) {
                  item.ExtensionDataDictionary["ship_commit_author"] = JObject.FromObject(commit.Author);
                }
                if (commit.Committer != null) {
                  item.ExtensionDataDictionary["ship_commit_committer"] = JObject.FromObject(commit.Committer);
                }
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
                    Task = ghc.Issue(repoName, issueNum, GitHubCacheDetails.Empty),
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
                    Task = ghc.PullRequest(repoName, prNum, GitHubCacheDetails.Empty),
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
              .Distinct(x => x.Login);
            var accountsParam = _mapper.Map<IEnumerable<AccountTableType>>(uniqueAccounts);
            changes.UnionWith(await context.BulkUpdateAccounts(timelineResponse.Date, accountsParam));

            var issueMessage = new TargetMessage(issueId.Value, user.Id);

            // Now safe to sync reactions
            tasks.Add(syncRepoIssueReactions.AddAsync(issueMessage));

            // If we find comments, sync them
            // TODO: Incrementally
            if (timeline.Any(x => x.Event == "commented")) {
              tasks.Add(syncIssueComments.AddAsync(issueMessage));
              // Can't sync comment reactions yet in case they don't exist
            }

            // Cleanup the data
            foreach (var item in filteredEvents) {
              // Oh GitHub, how I hate thee. Why can't you provide ids?
              // We're regularly seeing GitHub ids as large as 31 bits.
              // We can only store four things this way because we only have two free bits :(
              // TODO: HACK! THIS IS BRITTLE AND WILL BREAK!
              var ones31 = 0x7FFFFFFFL;
              var issuePart = (issueId.Value & ones31);
              if (issuePart != issueId.Value) {
                throw new NotSupportedException($"IssueId {issueId.Value} exceeds 31 bits!");
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
            var events = _mapper.Map<IEnumerable<IssueEventTableType>>(filteredEvents);

            // Set issueId
            foreach (var item in events) {
              item.IssueId = issueId.Value;
            }
            changes.UnionWith(await context.BulkUpdateTimelineEvents(user.Id, repoId, events, accountsParam.Select(x => x.Id)));
          }

          tasks.Add(notifyChanges.Send(changes));
          await Task.WhenAll(tasks);
        }
      });
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
