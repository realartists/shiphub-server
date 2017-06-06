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
  using ActorInterfaces;
  using AutoMapper;
  using Common;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Filters;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using QueueClient;
  using dm = Common.DataModel;

  [RoutePrefix("github")]
  public class GitHubProxyController : ApiController {
    // If you increase this, you may also need to update timeouts on the handler
    // For 60 seconds, the defaults are fine.
    private static readonly TimeSpan _ProxyTimeout = TimeSpan.FromSeconds(60);

    private static readonly HttpMessageHandler _ProxyHandler = HttpUtilities.CreateDefaultHandler(maxRedirects: 3);

    // Using one HttpClient for all requests should be safe according to the documentation.
    // See https://msdn.microsoft.com/en-us/library/system.net.http.httpclient(v=vs.110).aspx?f=255&mspperror=-2147217396#Anchor_5
    private static readonly HttpClient _ProxyClient = new HttpClient(_ProxyHandler) {
      MaxResponseContentBufferSize = 1024 * 1024 * 5, // 5MB is pretty generous
      Timeout = _ProxyTimeout
    };

    private static readonly HashSet<HttpMethod> _BareMethods = new HashSet<HttpMethod>() { HttpMethod.Get, HttpMethod.Head, HttpMethod.Options };

    private IMapper _mapper;
    private IShipHubQueueClient _queueClient;
    private IGrainFactory _grainFactory;
    private Uri _apiRoot;

    public GitHubProxyController(IShipHubConfiguration configuration, IMapper mapper, IShipHubQueueClient queueClient, IGrainFactory grainFactory) {
      _mapper = mapper;
      _queueClient = queueClient;
      _grainFactory = grainFactory;
      _apiRoot = configuration.GitHubApiRoot;
    }

    private async Task<HttpResponseMessage> ProxyRequest(HttpRequestMessage request, CancellationToken cancellationToken, string pathOverride) {
      var builder = new UriBuilder(request.RequestUri) {
        Scheme = _apiRoot.Scheme,
        Port = _apiRoot.Port,
        Host = _apiRoot.Host,
        Path = pathOverride
      };
      request.RequestUri = builder.Uri;

      request.Headers.Host = request.RequestUri.Host;
      // Authorization header passes through unaltered.

      // This is dumb
      if (_BareMethods.Contains(request.Method)) {
        request.Content = null;
      }

      using (var timeout = new CancellationTokenSource(_ProxyTimeout))
      using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token)) {
        try {
          var response = await _ProxyClient.SendAsync(request, linkedCancellation.Token);
          response.Headers.Remove("Server");
          return response;
        } catch (TaskCanceledException exception) {
          return request.CreateErrorResponse(HttpStatusCode.GatewayTimeout, exception);
        }
      }
    }

    // ////////////////////////////////////////////////////////////
    // Default Handler
    // ////////////////////////////////////////////////////////////

    [HttpDelete]
    [HttpGet]
    [HttpHead]
    [HttpOptions]
    [HttpPatch]
    [HttpPost]
    [HttpPut]
    [Route("{*path}")]
    public Task<HttpResponseMessage> ProxyBlind(HttpRequestMessage request, CancellationToken cancellationToken, string path) {
      return ProxyRequest(request, cancellationToken, path);
    }

    // ////////////////////////////////////////////////////////////
    // Deletions (comments, reviews, reactions, etc)
    // ////////////////////////////////////////////////////////////

    [HttpDelete]
    [Route("repos/{owner}/{repo}/issues/comments/{commentId:long}")]
    public async Task<HttpResponseMessage> DeleteComment(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, long commentId) {
      var response = await ProxyRequest(request, cancellationToken, $"repos/{owner}/{repo}/issues/comments/{commentId}");

      if (response.StatusCode == HttpStatusCode.NoContent) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          using (var context = new dm.ShipHubContext()) {
            var changes = await context.DeleteIssueComment(commentId);
            await changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpDelete]
    [Route("reactions/{reactionId:long}")]
    public async Task<HttpResponseMessage> DeleteReaction(HttpRequestMessage request, CancellationToken cancellationToken, long reactionId) {
      var response = await ProxyRequest(request, cancellationToken, $"reactions/{reactionId}");

      if (response.StatusCode == HttpStatusCode.NoContent) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          using (var context = new dm.ShipHubContext()) {
            var changes = await context.DeleteReaction(reactionId);
            await changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/pulls/{issueNumber:int}/reviews/{reviewId:long}")]
    public async Task<HttpResponseMessage> DeleteReview(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, int issueNumber, long reviewId) {
      var response = await ProxyRequest(request, cancellationToken, $"repos/{owner}/{repo}/pulls/{issueNumber}/reviews/{reviewId}");

      // WTF DELETE Review returns 200 OK
      // https://developer.github.com/v3/pulls/reviews/#delete-a-pending-review
      if (response.IsSuccessStatusCode) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          using (var context = new dm.ShipHubContext()) {
            var changes = await context.DeleteReview(reviewId);
            await changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/pulls/comments/{commentId:long}")]
    public async Task<HttpResponseMessage> DeleteReviewComment(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, long commentId) {
      var response = await ProxyRequest(request, cancellationToken, $"repos/{owner}/{repo}/pulls/comments/{commentId}");

      if (response.StatusCode == HttpStatusCode.NoContent) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          using (var context = new dm.ShipHubContext()) {
            var changes = await context.DeletePullRequestComment(commentId);
            await changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/pulls/{issueNumber:int}/requested_reviewers")]
    public async Task<HttpResponseMessage> DeleteRequestedReviewer(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, int issueNumber) {
      var response = await ProxyRequest(request, cancellationToken, $"repos/{owner}/{repo}/pulls/{issueNumber}/requested_reviewers");

      if (response.StatusCode == HttpStatusCode.OK) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var data = await response.Content.ReadAsAsync<JToken>(GitHubSerialization.MediaTypeFormatters, cancellationToken);
          var removed = data.Value<IEnumerable<string>>("reviewers");

          // TODO: This response actually contains the PR
          // Update our copy as appropriate

          using (var context = new dm.ShipHubContext()) {
            var changes = await context.DeleteReviewers($"{owner}/{repo}", issueNumber, removed);
            await changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    // ////////////////////////////////////////////////////////////
    // Issues
    // ////////////////////////////////////////////////////////////

    [HttpPost]
    [Route("repos/{owner}/{repo}/issues")]
    public async Task<HttpResponseMessage> IssueCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      var response = await ProxyRequest(request, cancellationToken, $"repos/{owner}/{repo}/issues");

      // Process the response
      if (response.StatusCode == HttpStatusCode.Created) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var issue = await response.Content.ReadAsAsync<Issue>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

          using (var context = new dm.ShipHubContext()) {
            var updater = new DataUpdater(context, _mapper);
            var repoName = $"{owner}/{repo}";
            var repoId = await context.Repositories
              .AsNoTracking()
              .Where(x => x.FullName == repoName)
              .Select(x => x.Id)
              .SingleAsync();

            await updater.UpdateIssues(repoId, response.Headers.Date ?? DateTimeOffset.UtcNow, new[] { issue });
            await updater.Changes.Submit(_queueClient);
          }

          // Trigger issue event and comment sync.
          var issueGrain = _grainFactory.GetGrain<IIssueActor>(issue.Number, $"{owner}/{repo}", grainClassNamePrefix: null);
          issueGrain.SyncInteractive(user.UserId).LogFailure(user.DebugIdentifier);
        } catch (Exception e) {
          // swallow db exceptions, since if we're here github has created the issue.
          // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }
  }
}
