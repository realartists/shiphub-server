namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces.GitHub;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Newtonsoft.Json.Linq;
  using QueueClient;
  using gm = Common.GitHub.Models;
  using sm = Sync.Messages.Entries;

  public class PullRequestCreateRequest {
    public IEnumerable<string> Assignees { get; set; }
    public int? Milestone { get; set; }
    public IEnumerable<string> Labels { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public string Head { get; set; }
    public string Base { get; set; }
  }

  [RoutePrefix("api/shiphub")]
  public class ShipHubController : ShipHubApiController {
    private IAsyncGrainFactory _grainFactory;
    private IMapper _mapper;
    private IShipHubQueueClient _queueClient;

    public ShipHubController(IAsyncGrainFactory grainFactory, IMapper mapper, IShipHubQueueClient queueClient) {
      _grainFactory = grainFactory;
      _mapper = mapper;
      _queueClient = queueClient;
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/pulls")]
    public async Task<IHttpActionResult> CreatePullRequest(
      HttpRequestMessage request,
      CancellationToken cancellationToken,
      [FromBody] PullRequestCreateRequest body,
      string owner,
      string repo) {

      GitHubResponse<gm.PullRequest> prResponse = null;
      GitHubResponse<gm.Issue> issueResponse = null;
      long? repoId = null;
      try {
        var ghc = await _grainFactory.GetGrain<IGitHubActor>(ShipHubUser.UserId);
        var repoName = $"{owner}/{repo}";

        prResponse = await ghc.CreatePullRequest(repoName, body.Title, body.Body, body.Base, body.Head, RequestPriority.Interactive);
        // Can't update the DB yet, because saving the PR requires the issue to already exist.

        if (prResponse.Status == HttpStatusCode.Created) {
          var pr = prResponse.Result;

          if (body.Milestone != null
            || body.Assignees?.Any() == true
            || body.Labels?.Any() == true) { // Have to patch
            issueResponse = await ghc.UpdateIssue(repoName, pr.Number, body.Milestone, body.Assignees, body.Labels, RequestPriority.Interactive);
          } else { // Lookup the issue
            issueResponse = await ghc.Issue(repoName, pr.Number, null, RequestPriority.Interactive);
          }

          if (issueResponse.IsOk) {
            // Ugh
            using (var context = new ShipHubContext()) {
              repoId = await context.Repositories
                .AsNoTracking()
                .Where(x => x.FullName == repoName)
                .Select(x => x.Id)
                .SingleAsync();

              var updater = new DataUpdater(context, _mapper);

              // Now we can update
              await updater.UpdateIssues(repoId.Value, issueResponse.Date, new[] { issueResponse.Result });
              await updater.UpdatePullRequests(repoId.Value, prResponse.Date, new[] { pr });

              await updater.Changes.Submit(_queueClient);
            }
          }
        }
      } catch (Exception e) {
        e.Report($"request: {request.RequestUri}", ShipHubUser.DebugIdentifier);
      }

      sm.PullRequestEntry prEntry = null;
      sm.IssueEntry issueEntry = null;

      if (issueResponse?.IsOk == true) {
        var issue = issueResponse.Result;
        issueEntry = new sm.IssueEntry() {
          Assignees = issue.Assignees.Select(x => x.Id).ToArray(),
          Body = issue.Body,
          ClosedAt = issue.ClosedAt,
          ClosedBy = issue.ClosedBy?.Id,
          CreatedAt = issue.CreatedAt,
          Identifier = issue.Id,
          Labels = issue.Labels.Select(x => x.Id).ToArray(),
          Locked = issue.Locked,
          Milestone = issue.Milestone?.Id,
          Number = issue.Number,
          PullRequest = issue.PullRequest != null,
          Repository = repoId ?? -1,
          ShipReactionSummary = issue.Reactions.SerializeObject().DeserializeObject<sm.ReactionSummary>(),
          State = issue.State,
          Title = issue.Title,
          UpdatedAt = issue.UpdatedAt,
          User = issue.User.Id,
        };
      }

      if (prResponse?.Status == HttpStatusCode.Created) {
        var pr = prResponse.Result;
        prEntry = new sm.PullRequestEntry() {
          Additions = pr.Additions,
          ChangedFiles = pr.ChangedFiles,
          Commits = pr.Commits,
          CreatedAt = pr.CreatedAt,
          Deletions = pr.Deletions,
          Identifier = pr.Id,
          Issue = issueEntry?.Identifier ?? -1,
          MaintainerCanModify = pr.MaintainerCanModify,
          Mergeable = pr.Mergeable,
          MergeableState = pr.MergeableState,
          MergeCommitSha = pr.MergeCommitSha,
          MergedAt = pr.MergedAt,
          MergedBy = pr.MergedBy?.Id,
          Rebaseable = pr.Rebaseable,
          RequestedReviewers = pr.RequestedReviewers.Select(x => x.Id).ToArray(),
          UpdatedAt = pr.UpdatedAt,
          Base = JToken.FromObject(pr.Base),
          Head = JToken.FromObject(pr.Head),
        };
      }

      var status = HttpStatusCode.Created;
      if (prEntry == null) {
        status = HttpStatusCode.InternalServerError;
      } else if (issueEntry == null) {
        status = HttpStatusCode.Accepted;
      }

      var result = new {
        PullRequest = prEntry,
        Issue = issueEntry,
      };

      return ResponseMessage(request.CreateResponse(status, result, JsonUtility.JsonMediaTypeFormatter));
    }
  }
}
