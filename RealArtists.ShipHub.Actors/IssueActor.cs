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
  using Orleans;
  using QueueClient;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using Common.GitHub;
  using Newtonsoft.Json.Linq;
  using gm = Common.GitHub.Models;

#if DEBUG
  using System.Diagnostics;
#endif

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

    // Comment sync
    private DateTimeOffset _latestComment;
    private GitHubMetadata _latestCommentMetadata;

    // Reaction sync
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
          .SingleOrDefaultAsync(x => x.Repository.FullName == _repoFullName && x.Number == _issueNumber);

        if (issue == null) {
          throw new InvalidOperationException($"Issue {_repoFullName}#{_issueNumber} does not exist and cannot be activated.");
        }

        _issueId = issue.Id;
        _repoId = issue.RepositoryId;

        _metadata = issue.Metadata;
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
        await Task.WhenAll(
          context.UpdateMetadata("Issues", "MetadataJson", _issueId, _metadata)
        );
      }
    }

    public async Task SyncInteractive(long forUserId) {
      ///////////////////////////////////////////
      /* NOTE!
       * We can't sync the timeline incrementally, because the client wants commit and
       * reference data inlined. This means we always have to download all the
       * timeline events in case an old one now has updated data. Other options are to
       * just be wrong, or to simply reference the user by id and mark them referenced
       * by the repo.
       */
      //////////////////////////////////////////

      // TODO: Load balance
      var ghc = _grainFactory.GetGrain<IGitHubActor>(forUserId);

      var tasks = new List<Task>();
      var changes = new ChangeSummary();
      using (var context = _contextFactory.CreateInstance()) {
        // Always refresh the issue when viewed
        var issueResponse = await ghc.Issue(_repoFullName, _issueNumber, _metadata);
        if (issueResponse.Status != HttpStatusCode.NotModified) {
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
            update.Labels?.Select(y => new LabelTableType() { ItemId = update.Id, Color = y.Color, Name = y.Name }),
            update.Assignees?.Select(y => new MappingTableType() { Item1 = update.Id, Item2 = y.Id })
          ));
        }

        _metadata = GitHubMetadata.FromResponse(issueResponse);

        // This will be cached per-user by the ShipHubFilter.
        var timelineResponse = await ghc.Timeline(_repoFullName, _issueNumber);
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

          // TODO: Use the comment entries from the timeline.
          // Don't force an additional sync.
          {
            //var issueMessage = new TargetMessage(issueId.Value, user.Id);

            //// Now safe to sync reactions
            //tasks.Add(syncRepoIssueReactions.AddAsync(issueMessage));

            //// If we find comments, sync them
            //if (timeline.Any(x => x.Event == "commented")) {
            //  tasks.Add(syncIssueComments.AddAsync(issueMessage));
            //  // Can't sync comment reactions yet in case they don't exist
            //}
          }

          // Cleanup the data
          foreach (var item in filteredEvents) {
            // Oh GitHub, how I hate thee. Why can't you provide ids?
            // We're regularly seeing GitHub ids as large as 31 bits.
            // We can only store four things this way because we only have two free bits :(
            // TODO: HACK! THIS IS BRITTLE AND WILL BREAK!
            var ones31 = 0x7FFFFFFFL;
            var issuePart = (_issueId & ones31);
            if (issuePart != _issueId) {
              throw new NotSupportedException($"IssueId {_issueId} exceeds 31 bits!");
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
            item.IssueId = _issueId;
          }
          changes.UnionWith(await context.BulkUpdateTimelineEvents(forUserId, _repoId, events, accountsParam.Select(x => x.Id)));
        }
      }

      if (!changes.Empty) {
        tasks.Add(_queueClient.NotifyChanges(changes));
      }

      await Task.WhenAll(tasks);
    }
  }
}
