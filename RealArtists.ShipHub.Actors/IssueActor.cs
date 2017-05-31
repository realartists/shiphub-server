namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using ActorInterfaces;
  using ActorInterfaces.GitHub;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using QueueClient;
  using gm = Common.GitHub.Models;

  public class IssueActor : Grain, IIssueActor {
    private IMapper _mapper;
    private IGrainFactory _grainFactory;
    private IFactory<ShipHubContext> _contextFactory;
    private IShipHubQueueClient _queueClient;

    // Issue Fields
    private long _issueId;
    private int _issueNumber;
    private long _repoId;
    private string _repoFullName;
    private bool _isPullRequest;

    private GitHubMetadata _metadata;
    private GitHubMetadata _commentMetadata;
    private GitHubMetadata _reactionMetadata;

    // Pull Request Fields
    private long? _prId;
    private string _prHeadRef;

    private GitHubMetadata _prMetadata;
    private GitHubMetadata _prCommentMetadata;
    private GitHubMetadata _prStatusMetadata;
    private GitHubMetadata _prMergeStatusMetadata;

    // Event sync
    private static readonly HashSet<string> _IgnoreTimelineEvents = new HashSet<string>(new[] {
      "commented",
      "commit-commented",
      "line-commented",
      "mentioned",
      "review_dismissed",
      "reviewed",
      "subscribed",
      "unsubscribed"
    }, StringComparer.OrdinalIgnoreCase);

    public IssueActor(IMapper mapper, IGrainFactory grainFactory, IFactory<ShipHubContext> contextFactory, IShipHubQueueClient queueClient) {
      _mapper = mapper;
      _grainFactory = grainFactory;
      _contextFactory = contextFactory;
      _queueClient = queueClient;
    }

    public override async Task OnActivateAsync() {
      _issueNumber = (int)this.GetPrimaryKeyLong(out _repoFullName);

      using (var context = _contextFactory.CreateInstance()) {
        var issue = await context.Issues
          .AsNoTracking()
          .SingleOrDefaultAsync(x => x.Repository.FullName == _repoFullName && x.Number == _issueNumber);

        if (issue == null) {
          throw new InvalidOperationException($"Issue {_repoFullName}#{_issueNumber} does not exist and cannot be activated.");
        }

        _issueId = issue.Id;
        _repoId = issue.RepositoryId;
        _isPullRequest = issue.PullRequest;

        _metadata = issue.Metadata;
        _commentMetadata = issue.CommentMetadata;
        _reactionMetadata = issue.ReactionMetadata;

        if (_isPullRequest) {
          var pullRequest = await context.PullRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IssueId == _issueId);

          _prId = pullRequest?.Id;
          _prHeadRef = pullRequest?.Head?.Sha;
          _prMetadata = pullRequest?.Metadata;
          _prCommentMetadata = pullRequest?.CommentMetadata;
          _prStatusMetadata = pullRequest?.StatusMetadata;
          _prMergeStatusMetadata = pullRequest?.MergeStatusMetadata;
        }
      }

      await base.OnActivateAsync();
    }

    public override async Task OnDeactivateAsync() {
      await Save();
      await base.OnDeactivateAsync();
    }

    private async Task Save() {
      using (var context = _contextFactory.CreateInstance()) {
        await context.UpdateMetadata("Issues", _issueId, _metadata);
        await context.UpdateMetadata("Issues", "CommentMetadataJson", _issueId, _commentMetadata);
        await context.UpdateMetadata("Issues", "ReactionMetadataJson", _issueId, _reactionMetadata);

        if (_prId.HasValue) {
          await context.UpdateMetadata("PullRequests", "MetadataJson", _prId.Value, _prMetadata);
          await context.UpdateMetadata("PullRequests", "CommentMetadataJson", _prId.Value, _prCommentMetadata);
          await context.UpdateMetadata("PullRequests", "StatusMetadataJson", _prId.Value, _prStatusMetadata);
          await context.UpdateMetadata("PullRequests", "MergeStatusMetadataJson", _prId.Value, _prMergeStatusMetadata);
        }
      }
    }

    public async Task SyncInteractive(long forUserId) {
      var ghc = _grainFactory.GetGrain<IGitHubActor>(forUserId);
      var changes = new ChangeSummary();
      try {
        await SyncIssueTimeline(ghc, changes, forUserId);
      } catch (GitHubRateException) {
        // nothing to do
      }

      if (!changes.IsEmpty) {
        await _queueClient.NotifyChanges(changes);
      }

      // Save metadata and other updates
      await Save();
    }

    private async Task SaveCommitStatuses(string gitRef, GitHubResponse<IEnumerable<gm.CommitStatus>> statusesResponse, ShipHubContext context, ChangeSummary changes) {
      if (statusesResponse.IsOk) {
        var statuses = statusesResponse.Result;
        var statusAccounts = statuses.Select(x => x.Creator).Distinct(x => x.Id);
        changes.UnionWith(await context.BulkUpdateAccounts(statusesResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(statusAccounts)));
        changes.UnionWith(await context.BulkUpdateCommitStatuses(_repoId, gitRef, _mapper.Map<IEnumerable<CommitStatusTableType>>(statuses)));
      }
    }

    private async Task SyncIssueTimeline(IGitHubActor ghc, ChangeSummary changes, long forUserId) {
      ///////////////////////////////////////////
      /* NOTE!
       * We can't sync the timeline incrementally, because the client wants commit and
       * reference data inlined. This means we always have to download all the
       * timeline events in case an old one now has updated data. Other options are to
       * just be wrong, or to simply reference the user by id and mark them referenced
       * by the repo.
       */
      //////////////////////////////////////////

      using (var context = _contextFactory.CreateInstance()) {
        // Always refresh the issue when viewed
        var issueResponse = await ghc.Issue(_repoFullName, _issueNumber, _metadata, RequestPriority.Interactive);
        if (issueResponse.IsOk) {
          var update = issueResponse.Result;

          // TODO: Unify this code with other issue update places to reduce bugs.
          _isPullRequest = update.PullRequest != null;  // Issues can become PRs

          var upAccounts = new[] { update.User, update.ClosedBy }.Concat(update.Assignees)
              .Where(x => x != null)
              .Distinct(x => x.Id);
          changes.UnionWith(await context.BulkUpdateAccounts(issueResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(upAccounts)));

          if (update.Milestone != null) {
            changes.UnionWith(await context.BulkUpdateMilestones(_repoId, _mapper.Map<IEnumerable<MilestoneTableType>>(new[] { update.Milestone })));
          }

          changes.UnionWith(await context.BulkUpdateIssues(
            _repoId,
            _mapper.Map<IEnumerable<IssueTableType>>(new[] { update }),
            update.Labels?.Select(y => new IssueMappingTableType() { IssueId = update.Id, IssueNumber = update.Number, MappedId = y.Id }),
            update.Assignees?.Select(y => new IssueMappingTableType() { IssueId = update.Id, IssueNumber = update.Number, MappedId = y.Id })
          ));
        }
        _metadata = GitHubMetadata.FromResponse(issueResponse);

        // If it's a PR we need that data too.
        if (_isPullRequest) {
          // Sadly, the PR info doesn't contain labels 😭
          var prResponseTask = ghc.PullRequest(_repoFullName, _issueNumber, _prMetadata, RequestPriority.Interactive);
          var prCommentsResponseTask = ghc.PullRequestComments(_repoFullName, _issueNumber, _prCommentMetadata, RequestPriority.Interactive);

          // Reviews and Review comments need to use the current user's token. Don't track metadata (yet - per user ideally)
          var prReviewsResponseTask = ghc.PullRequestReviews(_repoFullName, _issueNumber, priority: RequestPriority.Interactive);

          var prResponse = await prResponseTask;
          if (prResponse.IsOk) {
            var pr = prResponse.Result;
            _prId = pr.Id; // Issues can become PRs
            _prHeadRef = pr.Head.Sha;

            // Assignees, user, etc all handled by the issue endpoint.
            if (prResponse.Result.RequestedReviewers.Any()) {
              changes.UnionWith(await context.BulkUpdateAccounts(prResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(pr.RequestedReviewers)));
            }

            // Labels and milestones also handled by issue
            changes.UnionWith(await context.BulkUpdatePullRequests(
              _repoId,
              _mapper.Map<IEnumerable<PullRequestTableType>>(new[] { pr }),
              pr.RequestedReviewers.Select(y => new IssueMappingTableType(y.Id, _issueNumber, _issueId)))
            );
          }
          _prMetadata = GitHubMetadata.FromResponse(prResponse);

          // Reviews (has to come before comments, since PR comments reference reviews
          gm.Review myReview = null;
          var prReviewsResponse = await prReviewsResponseTask;
          if (prReviewsResponse.IsOk) {
            var reviews = prReviewsResponse.Result;
            myReview = reviews
             .Where(x => x.State == "PENDING")
             .FirstOrDefault(x => x.User.Id == forUserId);

            // Persist all to DB, regardless of state.
            // Sync will filter pending reviews

            var reviewAccounts = reviews.Select(x => x.User).Distinct(x => x.Id);
            changes.UnionWith(await context.BulkUpdateAccounts(prReviewsResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(reviewAccounts)));
            changes.UnionWith(await context.BulkUpdateReviews(_repoId, _issueId, prReviewsResponse.Date, forUserId, _mapper.Map<IEnumerable<ReviewTableType>>(reviews)));
          }

          // PR Comments
          var prCommentsResponse = await prCommentsResponseTask;
          if (prCommentsResponse.IsOk) {
            var prComments = prCommentsResponse.Result;

            var prAccounts = prComments.Select(x => x.User).Distinct(x => x.Id);
            changes.UnionWith(await context.BulkUpdateAccounts(prCommentsResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(prAccounts)));
            changes.UnionWith(await context.BulkUpdatePullRequestComments(_repoId, _issueId, _mapper.Map<IEnumerable<PullRequestCommentTableType>>(prComments)));
          }
          _prCommentMetadata = GitHubMetadata.FromResponse(prCommentsResponse);

          // Review Comments
          // Only fetch if *this user* has a pending review
          if (myReview != null) {
            var reviewCommentsResponse = await ghc.PullRequestReviewComments(_repoFullName, _issueNumber, myReview.Id, priority: RequestPriority.Interactive);
            if (reviewCommentsResponse.IsOk && reviewCommentsResponse.Result.Any()) {
              var reviewComments = reviewCommentsResponse.Result;
              var reviewCommentAccounts = reviewComments.Select(x => x.User).Distinct(x => x.Id);
              changes.UnionWith(await context.BulkUpdateAccounts(reviewCommentsResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(reviewCommentAccounts)));
              changes.UnionWith(await context.BulkUpdatePullRequestComments(_repoId, _issueId, _mapper.Map<IEnumerable<PullRequestCommentTableType>>(reviewComments), myReview.Id));
            }
          }

          // Commit Status
          var commitStatusesResponse = await ghc.CommitStatuses(_repoFullName, _prHeadRef, _prStatusMetadata, RequestPriority.Interactive);
          await SaveCommitStatuses(_prHeadRef, commitStatusesResponse, context, changes);
          _prStatusMetadata = GitHubMetadata.FromResponse(commitStatusesResponse);
        }

        // This will be cached per-user by the ShipHubFilter.
        var timelineResponse = await ghc.Timeline(_repoFullName, _issueNumber, _issueId, priority: RequestPriority.Interactive);
        if (timelineResponse.IsOk) {
          var timeline = timelineResponse.Result;

          // Now just filter
          var filteredEvents = timeline.Where(x => !_IgnoreTimelineEvents.Contains(x.Event)).ToArray();

          // For adding to the DB later
          var accounts = new List<gm.Account>();

          foreach (var tl in filteredEvents) {
            accounts.Add(tl.Actor);
            accounts.Add(tl.Assignee);
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
                  Task = ghc.Commit(repoName, sha, priority: RequestPriority.Interactive),
                };
              })
              .ToDictionary(x => x.Id, x => x.Task);

            // TODO: Lookup Repo Name->ID mapping

            await Task.WhenAll(commitLookups.Values);

            foreach (var item in withCommits) {
              var lookup = commitLookups[item.CommitUrl].Result;

              // best effort - requests will fail when the user doesn't have source access.
              // see Nick's account and references from the github-beta repo
              if (!lookup.IsOk) {
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

          var withSources = filteredEvents.Where(x => x.Source?.Url != null).ToArray();
          var sources = withSources.Select(x => x.Source.Url).Distinct();

          if (sources.Any()) {
            var sourceLookups = sources
              .Select(x => {
                var parts = x.Split('/');
                var numParts = parts.Length;
                var repoName = parts[numParts - 4] + "/" + parts[numParts - 3];
                var issueNum = int.Parse(parts[numParts - 1]);
                return new {
                  Id = x,
                  Task = ghc.Issue(repoName, issueNum, priority: RequestPriority.Interactive),
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
                  Task = ghc.PullRequest(repoName, prNum, priority: RequestPriority.Interactive),
                };
              })
              .ToDictionary(x => x.Id, x => x.Task);

            await Task.WhenAll(prLookups.Values);

            foreach (var item in withSources) {
              var refIssue = sourceLookups[item.Source.Url].Result.Result;
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

          // Fixup and sanity checks
          foreach (var item in filteredEvents) {
            switch (item.Event) {
              case "crossreferenced":
                if (item.Actor != null) { // It's a comment reference
                  accounts.Add(item.Source?.Actor);
                  item.Actor = item.Source?.Actor;
                } else { // It's an issue reference
                  accounts.Add(item.Source?.Issue?.User);
                  item.Actor = item.Source?.Issue?.User;
                }
                break;
              case "committed":
                item.CreatedAt = item.ExtensionDataDictionary["committer"]["date"].ToObject<DateTimeOffset>();
                break;
              default:
                // Leave most things alone.
                break;
            }

            if (item.CreatedAt == DateTimeOffset.MinValue) {
              throw new Exception($"Unable to process event of type {item.Event} on {_repoFullName}/{_issueNumber} ({_issueId}). Invalid date.");
            }
          }

          // Update accounts
          var uniqueAccounts = accounts
            .Where(x => x != null)
            .Distinct(x => x.Login);
          var accountsParam = _mapper.Map<IEnumerable<AccountTableType>>(uniqueAccounts);
          changes.UnionWith(await context.BulkUpdateAccounts(timelineResponse.Date, accountsParam));

          // This conversion handles the restriction field and hash.
          var events = _mapper.Map<IEnumerable<IssueEventTableType>>(filteredEvents);

          changes.UnionWith(await context.BulkUpdateTimelineEvents(forUserId, _repoId, events, accountsParam.Select(x => x.Id)));

          // Issue Reactions
          if (_reactionMetadata.IsExpired()) {
            var issueReactionsResponse = await ghc.IssueReactions(_repoFullName, _issueNumber, _reactionMetadata, RequestPriority.Interactive);
            if (issueReactionsResponse.IsOk) {
              var reactions = issueReactionsResponse.Result;

              var users = reactions
                .Select(x => x.User)
                .Distinct(x => x.Id);

              changes.UnionWith(
                await context.BulkUpdateAccounts(issueReactionsResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(users))
              );

              changes.UnionWith(await context.BulkUpdateIssueReactions(
                _repoId,
                _issueId,
                _mapper.Map<IEnumerable<ReactionTableType>>(reactions)));
            }

            _reactionMetadata = GitHubMetadata.FromResponse(issueReactionsResponse);
          }

          // Comments
          if (timeline.Any(x => x.Event == "commented")) {
            if (_commentMetadata.IsExpired()) {
              var commentResponse = await ghc.IssueComments(_repoFullName, _issueNumber, null, _commentMetadata, RequestPriority.Interactive);
              if (commentResponse.IsOk) {
                var comments = commentResponse.Result;

                var users = comments
                  .Select(x => x.User)
                  .Distinct(x => x.Id);
                changes.UnionWith(await context.BulkUpdateAccounts(commentResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(users)));

                foreach (var comment in comments) {
                  if (comment.IssueNumber == null) {
                    comment.IssueNumber = _issueNumber;
                  }
                }

                changes.UnionWith(await context.BulkUpdateIssueComments(
                  _repoId,
                  _mapper.Map<IEnumerable<CommentTableType>>(comments)));
              }

              _commentMetadata = GitHubMetadata.FromResponse(commentResponse);
            }
          }

          // Commit Comments
          // Comments in commit-commented events look complete.
          // Let's run with it.
          var commitCommentEvents = timeline.Where(x => x.Event == "commit-commented").ToArray();
          if (commitCommentEvents.Any()) {
            var commitComments = commitCommentEvents
              .SelectMany(x => x.ExtensionDataDictionary["comments"].ToObject<IEnumerable<gm.CommitComment>>(GitHubSerialization.JsonSerializer))
              .ToArray();

            var users = commitComments.Select(x => x.User).Distinct(x => x.Id);
            changes.UnionWith(await context.BulkUpdateAccounts(timelineResponse.Date, _mapper.Map<IEnumerable<AccountTableType>>(users)));

            changes.UnionWith(await context.BulkUpdateCommitComments(
              _repoId,
              _mapper.Map<IEnumerable<CommitCommentTableType>>(commitComments)));
          }

          // Merged event commit statuses
          if (_isPullRequest) {
            var mergedEvent = timeline
              .Where(x => x.Event == "merged")
              .OrderByDescending(x => x.CreatedAt)
              .FirstOrDefault();

            var mergeCommitId = mergedEvent?.CommitId;

            if (mergeCommitId != null) {
              var mergeCommitStatusesResponse = await ghc.CommitStatuses(_repoFullName, mergeCommitId, _prMergeStatusMetadata, RequestPriority.Interactive);
              await SaveCommitStatuses(mergeCommitId, mergeCommitStatusesResponse, context, changes);
              _prMergeStatusMetadata = GitHubMetadata.FromResponse(mergeCommitStatusesResponse);
            }
          }

        } // end timeline

        // Comment Reactions
        var commentReactionMetadata = await context.IssueComments
          .AsNoTracking()
          .Where(x => x.IssueId == _issueId)
          .ToDictionaryAsync(x => x.Id, x => x.ReactionMetadata);
        context.Database.Connection.Close();

        if (commentReactionMetadata.Any()) {
          // Now, find the ones that need updating.
          var commentReactionRequests = new Dictionary<long, Task<GitHubResponse<IEnumerable<gm.Reaction>>>>();
          foreach (var reactionMetadata in commentReactionMetadata) {
            if (reactionMetadata.Value.IsExpired()) {
              commentReactionRequests.Add(reactionMetadata.Key, ghc.IssueCommentReactions(_repoFullName, reactionMetadata.Key, reactionMetadata.Value, RequestPriority.Interactive));
            }
          }

          if (commentReactionRequests.Any()) {
            await Task.WhenAll(commentReactionRequests.Values);

            // TODO: Optimize this a lot.
            // Update all users at once
            // Update reactions as batch with single call to DB
            // Delete all comments at once.
            foreach (var commentReactionsResponse in commentReactionRequests) {
              var resp = await commentReactionsResponse.Value;
              switch (resp.Status) {
                case HttpStatusCode.NotModified:
                  break;
                case HttpStatusCode.NotFound:
                  // Deleted
                  changes.UnionWith(await context.DeleteIssueComment(commentReactionsResponse.Key));
                  break;
                default:
                  var reactions = resp.Result;

                  var users = reactions
                    .Select(x => x.User)
                    .Distinct(x => x.Id);
                  changes.UnionWith(await context.BulkUpdateAccounts(resp.Date, _mapper.Map<IEnumerable<AccountTableType>>(users)));

                  changes.UnionWith(await context.BulkUpdateIssueCommentReactions(
                    _repoId,
                    commentReactionsResponse.Key,
                    _mapper.Map<IEnumerable<ReactionTableType>>(reactions)));
                  break;
              }

              await context.UpdateMetadata("Comments", "ReactionMetadataJson", commentReactionsResponse.Key, resp);
            }
          }
        }

        // Commit Comment Reactions
        var committedEvents = await context.IssueEvents
          .AsNoTracking()
          .Where(x => x.IssueId == _issueId && x.Event == "committed")
          .ToArrayAsync();
        var commitShas = committedEvents
          .Select(x => x.ExtensionData.DeserializeObject<JToken>().Value<string>("sha"))
          .ToArray();

        if (commitShas.Any()) {
          var commitCommentCommentMetadata = await context.CommitComments
            .AsNoTracking()
            .Where(x => commitShas.Contains(x.CommitId))
            .ToDictionaryAsync(x => x.Id, x => x.ReactionMetadata);
          // I think... I think I threw up in my mouth a little.

          // Now, find the ones that need updating.
          var commentCommentReactionRequests = new Dictionary<long, Task<GitHubResponse<IEnumerable<gm.Reaction>>>>();
          foreach (var reactionMetadata in commitCommentCommentMetadata) {
            if (reactionMetadata.Value.IsExpired()) {
              commentCommentReactionRequests.Add(reactionMetadata.Key, ghc.CommitCommentReactions(_repoFullName, reactionMetadata.Key, reactionMetadata.Value, RequestPriority.Interactive));
            }
          }

          if (commentCommentReactionRequests.Any()) {
            await Task.WhenAll(commentCommentReactionRequests.Values);

            // Shame. The weight of it...
            foreach (var commitCommentReactionsResponse in commentCommentReactionRequests) {
              var resp = await commitCommentReactionsResponse.Value;
              switch (resp.Status) {
                case HttpStatusCode.NotModified:
                  break;
                case HttpStatusCode.NotFound:
                  // Deleted
                  changes.UnionWith(await context.DeleteCommitComment(commitCommentReactionsResponse.Key));
                  break;
                default:
                  var reactions = resp.Result;

                  var users = reactions
                    .Select(x => x.User)
                    .Distinct(x => x.Id);
                  changes.UnionWith(await context.BulkUpdateAccounts(resp.Date, _mapper.Map<IEnumerable<AccountTableType>>(users)));

                  changes.UnionWith(await context.BulkUpdateCommitCommentReactions(
                    _repoId,
                    commitCommentReactionsResponse.Key,
                    _mapper.Map<IEnumerable<ReactionTableType>>(reactions)));
                  break;
              }

              await context.UpdateMetadata("CommitComments", "ReactionMetadataJson", commitCommentReactionsResponse.Key, resp);
            }
          }
        }

        // Pull Request comment reactions
        if (_isPullRequest) {
          // Oh dear - need to overhaul this and the above code.
          var prcReactionMetadata = await context.PullRequestComments
            .AsNoTracking()
            .Where(x => x.IssueId == _issueId)
            .ToDictionaryAsync(x => x.Id, x => x.ReactionMetadata);
          context.Database.Connection.Close();

          // Now, find the ones that need updating.
          var prcReactionRequests = new Dictionary<long, Task<GitHubResponse<IEnumerable<gm.Reaction>>>>();
          foreach (var reactionMetadata in prcReactionMetadata) {
            if (reactionMetadata.Value.IsExpired()) {
              prcReactionRequests.Add(reactionMetadata.Key, ghc.PullRequestCommentReactions(_repoFullName, reactionMetadata.Key, reactionMetadata.Value, RequestPriority.Interactive));
            }
          }

          await Task.WhenAll(prcReactionRequests.Values);

          // TODO: Optimize this a lot.
          // Update all users at once
          // Update reactions as batch with single call to DB
          // Delete all comments at once.
          foreach (var prcReactionsResponse in prcReactionRequests) {
            var resp = await prcReactionsResponse.Value;
            switch (resp.Status) {
              case HttpStatusCode.NotModified:
                break;
              case HttpStatusCode.NotFound:
                // Deleted
                changes.UnionWith(await context.DeletePullRequestComment(prcReactionsResponse.Key));
                break;
              default:
                var reactions = resp.Result;

                var users = reactions
                  .Select(x => x.User)
                  .Distinct(x => x.Id);
                changes.UnionWith(await context.BulkUpdateAccounts(resp.Date, _mapper.Map<IEnumerable<AccountTableType>>(users)));

                changes.UnionWith(await context.BulkUpdatePullRequestCommentReactions(
                  _repoId,
                  prcReactionsResponse.Key,
                  _mapper.Map<IEnumerable<ReactionTableType>>(reactions)));
                break;
            }

            await context.UpdateMetadata("PullRequestComments", "ReactionMetadataJson", prcReactionsResponse.Key, resp);
          }
        }
      }
    }
  }
}
