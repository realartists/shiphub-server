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

    private string _repoFullName;
    private int _issueNumber;

    private long _repoId;
    private long _issueId;

    private GitHubMetadata _metadata;
    private GitHubMetadata _commentMetadata;
    private GitHubMetadata _reactionMetadata;

    // Event sync
    private static readonly HashSet<string> _IgnoreTimelineEvents = new HashSet<string>(new[] { "commented", "subscribed", "unsubscribed" }, StringComparer.OrdinalIgnoreCase);

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

        _metadata = issue.Metadata;
        _commentMetadata = issue.CommentMetadata;
        _reactionMetadata = issue.ReactionMetadata;
      }

      await base.OnActivateAsync();
    }

    public override async Task OnDeactivateAsync() {
      await Save();
      await base.OnDeactivateAsync();
    }

    private async Task Save() {
      using (var context = _contextFactory.CreateInstance()) {
        // context only supports one operation at a time
        await context.UpdateMetadata("Issues", _issueId, _metadata);
        await context.UpdateMetadata("Issues", "CommentMetadataJson", _issueId, _commentMetadata);
        await context.UpdateMetadata("Issues", "ReactionMetadataJson", _issueId, _reactionMetadata);
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
            update.Labels?.Select(y => new MappingTableType() { Item1 = update.Id, Item2 = y.Id }),
            update.Assignees?.Select(y => new MappingTableType() { Item1 = update.Id, Item2 = y.Id })
          ));
        }

        _metadata = GitHubMetadata.FromResponse(issueResponse);

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
              case "reviewed":
                item.CreatedAt = item.SubmittedAt.Value;
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
              var commentResponse = await ghc.Comments(_repoFullName, _issueNumber, null, _commentMetadata, RequestPriority.Interactive);
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

                changes.UnionWith(await context.BulkUpdateComments(
                  _repoId,
                  _mapper.Map<IEnumerable<CommentTableType>>(comments)));
              }

              _commentMetadata = GitHubMetadata.FromResponse(commentResponse);
            }
          }
        }

        // Comment Reactions
        var commentReactionMetadata = await context.Comments
          .AsNoTracking()
          .Where(x => x.IssueId == _issueId)
          .ToDictionaryAsync(x => x.Id, x => x.ReactionMetadata);
        context.Database.Connection.Close();

        // Now, find the ones that need updating.
        var commentReactionRequests = new Dictionary<long, Task<GitHubResponse<IEnumerable<gm.Reaction>>>>();
        foreach (var reactionMetadata in commentReactionMetadata) {
          if (reactionMetadata.Value.IsExpired()) {
            commentReactionRequests.Add(reactionMetadata.Key, ghc.IssueCommentReactions(_repoFullName, reactionMetadata.Key, reactionMetadata.Value, RequestPriority.Interactive));
          }
        }

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
              changes.UnionWith(await context.DeleteComments(new[] { commentReactionsResponse.Key }));
              break;
            default:
              var reactions = resp.Result;

              var users = reactions
                .Select(x => x.User)
                .Distinct(x => x.Id);
              changes.UnionWith(await context.BulkUpdateAccounts(resp.Date, _mapper.Map<IEnumerable<AccountTableType>>(users)));

              changes.UnionWith(await context.BulkUpdateCommentReactions(
                _repoId,
                commentReactionsResponse.Key,
                _mapper.Map<IEnumerable<ReactionTableType>>(reactions)));
              break;
          }

          await context.UpdateMetadata("Comments", "ReactionMetadataJson", commentReactionsResponse.Key, resp);
        }
      }
    }
  }
}
