namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;
  using ActorInterfaces;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Common.GitHub.Models.WebhookPayloads;
  using Orleans;
  using Orleans.Concurrency;
  using QueueClient;

  [Reentrant]
  [StatelessWorker]
  public class WebhookEventActor : Grain, IWebhookEventActor {
    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    public WebhookEventActor(IMapper mapper, IGrainFactory grainFactory, IFactory<ShipHubContext> contextFactory, IShipHubQueueClient queueClient) {
      _mapper = mapper;
      _grainFactory = grainFactory;
      _contextFactory = contextFactory;
      _queueClient = queueClient;
    }

    public async Task CommitComment(DateTimeOffset eventDate, CommitCommentPayload payload) {
      using (var context = _contextFactory.CreateInstance()) {
        var updater = new DataUpdater(context, _mapper);

        await updater.UpdateRepositories(eventDate, new[] { payload.Repository });

        // NOTE: As of this commit, edited and deleted are never sent.
        // We're ready to support it if they're added later
        switch (payload.Action) {
          case "created":
          case "edited":
            await updater.UpdateCommitComments(payload.Repository.Id, eventDate, new[] { payload.Comment });
            break;
          case "deleted":
            await updater.DeleteCommitComment(payload.Comment.Id);
            break;
          default:
            throw new NotImplementedException($"Action '{payload.Action}' is not valid for event {nameof(CommitComment)}.");
        }

        await updater.Changes.Submit(_queueClient);
      }
    }

    public async Task IssueComment(DateTimeOffset eventDate, IssueCommentPayload payload) {
      using (var context = _contextFactory.CreateInstance()) {
        var updater = new DataUpdater(context, _mapper);

        // Ensure the issue that owns this comment exists locally before we add the comment.
        await updater.UpdateRepositories(eventDate, new[] { payload.Repository });
        await updater.UpdateIssues(payload.Repository.Id, eventDate, new[] { payload.Issue });

        switch (payload.Action) {
          case "created":
            await updater.UpdateIssueComments(payload.Repository.Id, eventDate, new[] { payload.Comment });
            break;
          case "edited":
            // GitHub doesn't send the new comment body. We have to look it up ourselves.
            var repoActor = _grainFactory.GetGrain<IRepositoryActor>(payload.Repository.Id);
            await repoActor.RefreshIssueComment(payload.Comment.Id);
            break;
          case "deleted":
            await updater.DeleteIssueComment(payload.Comment.Id);
            break;
          default:
            throw new NotImplementedException($"Action '{payload.Action}' is not valid for event {nameof(IssueComment)}.");
        }

        await updater.Changes.Submit(_queueClient);
      }
    }

    public async Task Issues(DateTimeOffset eventDate, IssuesPayload payload) {
      switch (payload.Action) {
        case "assigned":
        case "unassigned":
        case "labeled":
        case "unlabeled":
        case "opened":
        case "edited":
        case "milestoned":
        case "demilestoned":
        case "closed":
        case "reopened":
          using (var context = _contextFactory.CreateInstance()) {
            var updater = new DataUpdater(context, _mapper);
            // TODO: Update Org and Sender?
            await updater.UpdateRepositories(eventDate, new[] { payload.Repository });
            await updater.UpdateIssues(payload.Repository.Id, eventDate, new[] { payload.Issue });

            await updater.Changes.Submit(_queueClient);
          }
          break;
        default:
          throw new NotImplementedException($"Action '{payload.Action}' is not valid for event {nameof(Issues)}.");
      }
    }

    public async Task Label(DateTimeOffset eventDate, LabelPayload payload) {
      using (var context = _contextFactory.CreateInstance()) {
        var updater = new DataUpdater(context, _mapper);

        await updater.UpdateRepositories(eventDate, new[] { payload.Repository });

        switch (payload.Action) {
          case "created":
          case "edited":
            await updater.UpdateLabels(payload.Repository.Id, new[] { payload.Label });
            break;
          case "deleted":
            await updater.DeleteLabel(payload.Label.Id);
            break;
          default:
            throw new NotImplementedException($"Action '{payload.Action}' is not valid for event {nameof(Label)}.");
        }

        await updater.Changes.Submit(_queueClient);
      }
    }

    public async Task Milestone(DateTimeOffset eventDate, MilestonePayload payload) {
      using (var context = _contextFactory.CreateInstance()) {
        var updater = new DataUpdater(context, _mapper);

        await updater.UpdateRepositories(eventDate, new[] { payload.Repository });

        switch (payload.Action) {
          case "created":
          case "closed":
          case "opened":
          case "edited":
            await updater.UpdateMilestones(payload.Repository.Id, eventDate, new[] { payload.Milestone });
            break;
          case "deleted":
            await updater.DeleteMilestone(payload.Milestone.Id);
            break;
          default:
            throw new NotImplementedException($"Action '{payload.Action}' is not valid for event {nameof(Milestone)}.");
        }

        await updater.Changes.Submit(_queueClient);
      }
    }

    public async Task PullRequestReviewComment(DateTimeOffset eventDate, PullRequestReviewCommentPayload payload) {
      using (var context = _contextFactory.CreateInstance()) {
        var updater = new DataUpdater(context, _mapper);

        await updater.UpdateRepositories(eventDate, new[] { payload.Repository });
        await updater.UpdatePullRequests(payload.Repository.Id, eventDate, new[] { payload.PullRequest });

        switch (payload.Action) {
          case "created":
            // We need the issueId
            // TODO: Modify BulkUpdate to work with PR/Issue Number instead?
            var issueId = await context.Issues
              .AsNoTracking()
              .Where(x => x.RepositoryId == payload.Repository.Id && x.Number == payload.PullRequest.Number)
              .Select(x => (long?)x.Id)
              .SingleOrDefaultAsync();

            if (issueId != null) {
              await updater.UpdatePullRequestComments(payload.Repository.Id, issueId.Value, eventDate, new[] { payload.Comment }, dropWithMissingReview: true);
            }
            break;
          case "edited":
            // GitHub doesn't send the new comment body. We have to look it up ourselves.
            var repoActor = _grainFactory.GetGrain<IRepositoryActor>(payload.Repository.Id);
            await repoActor.RefreshPullRequestReviewComment(payload.Comment.Id);
            break;
          case "deleted":
            await updater.DeletePullRequestComment(payload.Comment.Id);
            break;
          default:
            throw new NotImplementedException($"Action '{payload.Action}' is not valid for event {nameof(PullRequestReviewComment)}.");
        }

        await updater.Changes.Submit(_queueClient);
      }
    }

    public async Task PullRequestReview(DateTimeOffset eventDate, PullRequestReviewPayload payload) {
      using (var context = _contextFactory.CreateInstance()) {
        var updater = new DataUpdater(context, _mapper);

        await updater.UpdateRepositories(eventDate, new[] { payload.Repository });
        await updater.UpdatePullRequests(payload.Repository.Id, eventDate, new[] { payload.PullRequest });

        switch (payload.Action) {
          case "submitted":
          case "edited":
            // We need the issueId
            // TODO: Modify BulkUpdate to work with PR/Issue Number instead?
            var issueId = await context.Issues
              .AsNoTracking()
              .Where(x => x.RepositoryId == payload.Repository.Id && x.Number == payload.PullRequest.Number)
              .Select(x => (long?)x.Id)
              .SingleOrDefaultAsync();

            if (issueId != null) {
              await updater.UpdateReviews(payload.Repository.Id, issueId.Value, eventDate, new[] { payload.Review });
            }
            break;
          case "dismissed":
            await updater.DeleteReview(payload.Review.Id);
            break;
          default:
            throw new NotImplementedException($"Action '{payload.Action}' is not valid for event {nameof(PullRequestReview)}.");
        }

        await updater.Changes.Submit(_queueClient);
      }
    }

    public async Task PullRequest(DateTimeOffset eventDate, PullRequestPayload payload) {
      switch (payload.Action) {
        case "assigned":
        case "unassigned":
        case "review_requested":
        case "review_request_removed":
        case "labeled":
        case "unlabeled":
        case "opened":
        case "edited":
        case "closed":
        case "reopened":
        case "synchronize":
          // For all actions:
          using (var context = _contextFactory.CreateInstance()) {
            var updater = new DataUpdater(context, _mapper);
            // TODO: Update Org and Sender?
            await updater.UpdateRepositories(eventDate, new[] { payload.Repository });
            await updater.UpdatePullRequests(payload.Repository.Id, eventDate, new[] { payload.PullRequest });

            await updater.Changes.Submit(_queueClient);

            // For *only* synchronize, go a generic timeline sync:
            if (payload.Action == "synchronize") {
              var issueNumber = await context.PullRequests
                .AsNoTracking()
                .Where(x => x.Id == payload.PullRequest.Id)
                .Select(x => (int?)x.Number)
                .FirstOrDefaultAsync();

              if (issueNumber.HasValue) {
                var actor = _grainFactory.GetGrain<IIssueActor>(issueNumber.Value, payload.Repository.FullName, grainClassNamePrefix: null);
                await actor.SyncTimeline(null, RequestPriority.Background);
              }
            }
          }
          break;
        default:
          throw new NotImplementedException($"Action '{payload.Action}' is not valid for event {nameof(PullRequest)}.");
      }
    }

    public Task Push(DateTimeOffset eventDate, PushPayload payload) {
      var defaultBranch = payload.Repository.DefaultBranch ?? "";
      var defaultRef = $"refs/heads/{defaultBranch}";
      if (defaultRef != payload.Ref) {
        return Task.CompletedTask; // if not a push to default branch then we don't care
      }

      bool hasIssueOrPRTemplate;
      if (payload.Size > payload.Commits.Count()) {
        hasIssueOrPRTemplate = true; // if we can't enumerate all the commits, assume ISSUE_TEMPLATE was changed.
      } else {
        // the payload includes all of the commits. take a look and see if we can find the ISSUE_TEMPLATE.md in any of the edited files
        hasIssueOrPRTemplate = payload.Commits
          .Aggregate(new HashSet<string>(), (accum, commit) => {
            accum.UnionWith(commit.Added);
            accum.UnionWith(commit.Modified);
            accum.UnionWith(commit.Removed);
            return accum;
          })
          .Any(f => RepositoryActor.EndsWithIssueTemplateRegex.IsMatch(f) || RepositoryActor.EndsWithPullRequestTemplateRegex.IsMatch(f));
      }

      if (hasIssueOrPRTemplate) {
        var repoActor = _grainFactory.GetGrain<IRepositoryActor>(payload.Repository.Id);
        return repoActor.SyncIssueTemplate();
      } else {
        return Task.CompletedTask;
      }
    }

    public async Task Repository(DateTimeOffset eventDate, RepositoryPayload payload) {
      using (var context = _contextFactory.CreateInstance()) {
        var updater = new DataUpdater(context, _mapper);

        switch (payload.Action) {
          case "created":
          case "publicized":
          case "privatized":
            await updater.UpdateRepositories(eventDate, new[] { payload.Repository });
            break;
          case "deleted": {
              // Sync contributors on deletion
              try {
                // Use the account links before we delete them
                var repoActor = _grainFactory.GetGrain<IRepositoryActor>(payload.Repository.Id);
                await repoActor.ForceSyncAllLinkedAccountRepositories();
              } catch (InvalidOperationException) {
                // If the repo has already been deleted it can't be activated.
              }

              // Now delete
              await updater.DeleteRepository(payload.Repository.Id);
            }
            break;
          default:
            throw new NotImplementedException($"Action '{payload.Action}' is not valid for event {nameof(Repository)}.");
        }

        // Sync org members on both creation and deletion
        if (payload.Action == "created" || payload.Action == "deleted") {
          if (payload.Repository.Owner.Type == GitHubAccountType.Organization) {
            var orgActor = _grainFactory.GetGrain<IOrganizationActor>(payload.Repository.Owner.Id);
            orgActor.ForceSyncAllMemberRepositories().LogFailure(); // Best effort only
          }
        }

        await updater.Changes.Submit(_queueClient);
      }
    }

    public async Task Status(DateTimeOffset eventDate, StatusPayload payload) {
      using (var context = _contextFactory.CreateInstance()) {
        var updater = new DataUpdater(context, _mapper);

        await updater.UpdateRepositories(eventDate, new[] { payload.Repository });

        // Since we only do this here there's no need to add a mapping
        var status = new CommitStatus() {
          Context = payload.Context,
          CreatedAt = payload.CreatedAt,
          Creator = payload.Sender,
          Description = payload.Description,
          Id = payload.Id,
          State = payload.State,
          TargetUrl = payload.TargetUrl,
          UpdatedAt = payload.UpdatedAt,
        };
        await updater.UpdateCommitStatuses(payload.Repository.Id, payload.Sha, new[] { status });

        await updater.Changes.Submit(_queueClient);
      }
    }
  }
}
