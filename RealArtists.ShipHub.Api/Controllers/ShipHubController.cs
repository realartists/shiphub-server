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
  using AutoMapper;
  using Common;
  using Common.GitHub;
  using Orleans;
  using RealArtists.ShipHub.ActorInterfaces.GitHub;
  using RealArtists.ShipHub.Common.DataModel;
  using RealArtists.ShipHub.QueueClient;
  using gm = Common.GitHub.Models;

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
    private IGrainFactory _grainFactory;
    private IMapper _mapper;
    private IShipHubQueueClient _queueClient;

    public ShipHubController(IGrainFactory grainFactory, IMapper mapper, IShipHubQueueClient queueClient) {
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

      try {
        using (var context = new ShipHubContext()) {
          var updater = new DataUpdater(context, _mapper);
          var ghc = _grainFactory.GetGrain<IGitHubActor>(ShipHubUser.UserId);
          var repoName = $"{owner}/{repo}";

          var createPrResponse = await ghc.CreatePullRequest(repoName, body.Head, body.Base, body.Body, RequestPriority.Interactive);
          if (!createPrResponse.IsOk) { return StatusCode(createPrResponse.Status); }
          // Can't update the DB yet, because saving the PR requires the issue to already exist.

          var pr = createPrResponse.Result;
          GitHubResponse<gm.Issue> issueResponse = null;

          if (body.Milestone != null
              || body.Assignees?.Any() == true
              || body.Labels?.Any() == true) {
            // Have to patch
            issueResponse = await ghc.UpdateIssue(repoName, pr.Number, body.Milestone, body.Assignees, body.Labels, RequestPriority.Interactive);
          } else { // Lookup the issue
            issueResponse = await ghc.Issue(repoName, pr.Number, null, RequestPriority.Interactive);
          }
          if (!issueResponse.IsOk) { return StatusCode(createPrResponse.Status); }

          // Ugh
          var repoId = await context.Repositories
            .AsNoTracking()
            .Where(x => x.FullName == repoName)
            .Select(x => x.Id)
            .SingleAsync();

          // Now we can update
          await updater.UpdateIssues(repoId, issueResponse.Date, new[] { issueResponse.Result });
          await updater.UpdatePullRequests(repoId, createPrResponse.Date, new[] { pr });

          await updater.Changes.Submit(_queueClient);

          // TODO: Check if James wants GitHub or SaneSerializer settings.
          return ResponseMessage(request.CreateResponse(HttpStatusCode.Created, pr, GitHubSerialization.JsonMediaTypeFormatter));
        }
      } catch (Exception e) {
        e.Report($"request: {request.RequestUri} user: {ShipHubUser.DebugIdentifier}", ShipHubUser.DebugIdentifier);
        throw;
      }
    }
  }
}
