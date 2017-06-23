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
  using Orleans;
  using QueueClient;
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

      GitHubResponse<gm.PullRequest> prResponse = null;
      GitHubResponse<gm.Issue> issueResponse = null;
      try {
        using (var context = new ShipHubContext()) {
          var updater = new DataUpdater(context, _mapper);
          var ghc = _grainFactory.GetGrain<IGitHubActor>(ShipHubUser.UserId);
          var repoName = $"{owner}/{repo}";

          prResponse = await ghc.CreatePullRequest(repoName, body.Title, body.Body, body.Base, body.Head, RequestPriority.Interactive);
          // Can't update the DB yet, because saving the PR requires the issue to already exist.

          if (prResponse.IsOk) {
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
              var repoId = await context.Repositories
                .AsNoTracking()
                .Where(x => x.FullName == repoName)
                .Select(x => x.Id)
                .SingleAsync();

              // Now we can update
              await updater.UpdateIssues(repoId, issueResponse.Date, new[] { issueResponse.Result });
              await updater.UpdatePullRequests(repoId, prResponse.Date, new[] { pr });

              await updater.Changes.Submit(_queueClient);
            }
          }
        }
      } catch (Exception e) {
        e.Report($"request: {request.RequestUri} user: {ShipHubUser.DebugIdentifier}", ShipHubUser.DebugIdentifier);
      }

      var status = (prResponse?.IsOk == true && issueResponse?.IsOk == true) ? HttpStatusCode.Created : HttpStatusCode.InternalServerError;
      var result = new {
        PullRequest = prResponse?.IsOk == true ? prResponse.Result : null,
        Issue = issueResponse?.IsOk == true ? issueResponse.Result : null,
      };
      return ResponseMessage(request.CreateResponse(status, result, GitHubSerialization.JsonMediaTypeFormatter));
    }
  }
}
