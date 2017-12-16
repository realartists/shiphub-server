namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using AutoMapper;
  using Common;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Filters;
  using Newtonsoft.Json.Linq;
  using QueueClient;
  using dm = Common.DataModel;

  [RoutePrefix("github")]
  public class GitHubProxyController : ApiController {
    // If you increase this, you may also need to update timeouts on the handler
    // For 60 seconds, the defaults are fine.
    private static readonly TimeSpan _ProxyTimeout = TimeSpan.FromSeconds(60);

    private static readonly HttpMessageHandler _ProxyHandler = HttpUtilities.CreateDefaultHandler(maxRedirects: 3);

    private const string PersonalAccessTokenHeader = "X-Authorization-PAT";

    // Using one HttpClient for all requests should be safe according to the documentation.
    // See https://msdn.microsoft.com/en-us/library/system.net.http.httpclient(v=vs.110).aspx?f=255&mspperror=-2147217396#Anchor_5
    private static readonly HttpClient _ProxyClient = new HttpClient(_ProxyHandler) {
      MaxResponseContentBufferSize = 1024 * 1024 * 5, // 5MB is pretty generous
      Timeout = _ProxyTimeout
    };

    private static readonly HashSet<HttpMethod> _BareMethods = new HashSet<HttpMethod>() { HttpMethod.Get, HttpMethod.Head, HttpMethod.Options };

    private IMapper _mapper;
    private IShipHubQueueClient _queueClient;
    private Uri _apiRoot;

    public GitHubProxyController(IShipHubConfiguration configuration, IMapper mapper, IShipHubQueueClient queueClient) {
      _mapper = mapper;
      _queueClient = queueClient;
      _apiRoot = configuration.GitHubApiRoot;
    }

    private async Task<HttpResponseMessage> ProxyRequest(HttpRequestMessage request, CancellationToken cancellationToken) {
      // Strip leading /github/
      var path = string.Concat(Request.RequestUri.Segments.Skip(2)); // leading '/' counts as its own segment
      var query = Request.RequestUri.Query;
      var builder = new UriBuilder(request.RequestUri) {
        Scheme = _apiRoot.Scheme,
        Port = _apiRoot.Port,
        Host = _apiRoot.Host,
        Path = path,
        Query = query,
      };
      request.RequestUri = builder.Uri;

      request.Headers.Host = request.RequestUri.Host;

      // Authorization header passes through unaltered, unless overridden by Personal Access Token
      var pat = request.Headers.ParseHeader(PersonalAccessTokenHeader, x => x);
      if (!string.IsNullOrWhiteSpace(pat)
        && AuthenticationHeaderValue.TryParse(pat, out var patHeader)) {
        request.Headers.Authorization = patHeader;
        request.Headers.Remove(PersonalAccessTokenHeader);
      }

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
    // Logs all traffic to catch things we've missed.
    // ////////////////////////////////////////////////////////////

    [HttpDelete]
    [HttpGet]
    [HttpHead]
    [HttpOptions]
    [HttpPatch]
    [HttpPost]
    [HttpPut]
    [Route("{*path}")]
    public Task<HttpResponseMessage> ProxyBlind(HttpRequestMessage request, CancellationToken cancellationToken) {
      Log.Error($"Unexpected proxy path: {Request.RequestUri.AbsolutePath}");
      return ProxyRequest(request, cancellationToken);
    }

    // ////////////////////////////////////////////////////////////
    // Notifications
    // ////////////////////////////////////////////////////////////

    [HttpPatch]
    [Route("notifications/threads/{id:int}")]
    public Task<HttpResponseMessage> NotificationsMarkThreadAsRead(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/activity/notifications/#mark-a-thread-as-read
      return ProxyRequest(request, cancellationToken);
    }

    [HttpPut]
    [Route("notifications")]
    public Task<HttpResponseMessage> NotificationsMarkAsRead(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/activity/notifications/#mark-as-read
      return ProxyRequest(request, cancellationToken);
    }

    // ////////////////////////////////////////////////////////////
    // Reactions
    // ////////////////////////////////////////////////////////////

    //private async Task<HttpResponseMessage> ProxyReaction(HttpRequestMessage request, CancellationToken cancellationToken) {
    //  var response = await ProxyRequest(request, cancellationToken);

    //  if (response.IsSuccessStatusCode) {
    //    var user = RequestContext.Principal as ShipHubPrincipal;
    //    try {
    //      await response.Content.LoadIntoBufferAsync();
    //      var issue = await response.Content.ReadAsAsync<Reaction>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

    //      using (var context = new dm.ShipHubContext()) {
    //        var updater = new DataUpdater(context, _mapper);
    //        var repoName = $"{owner}/{repo}";
    //        var repoId = await context.Repositories
    //          .AsNoTracking()
    //          .Where(x => x.FullName == repoName)
    //          .Select(x => x.Id)
    //          .SingleAsync();

    //        await updater.UpdateReactions(repoId, response.Headers.Date ?? DateTimeOffset.UtcNow, new[] { issue });
    //        await updater.Changes.Submit(_queueClient);
    //      }
    //    } catch (Exception e) {
    //      // swallow db exceptions, since if we're here github has created the resource.
    //      // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
    //      e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
    //    }
    //  }

    //  return response;
    //}

    [HttpPost]
    [Route("repos/{owner}/{repo}/comments/{id:long}/reactions")]
    public Task<HttpResponseMessage> ReactionCommitCommentCreate(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/reactions/#create-reaction-for-a-commit-comment
      return ProxyRequest(request, cancellationToken);
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/issues/{number:int}/reactions")]
    public Task<HttpResponseMessage> ReactionIssueCreate(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/reactions/#create-reaction-for-an-issue
      return ProxyRequest(request, cancellationToken);
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/issues/comments/{commentId:long}/reactions")]
    public Task<HttpResponseMessage> ReactionIssueCommentCreate(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/reactions/#create-reaction-for-an-issue-comment
      return ProxyRequest(request, cancellationToken);
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/pulls/comments/{commentId:long}/reactions")]
    public Task<HttpResponseMessage> ReactionPullRequestCommentCreate(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/reactions/#create-reaction-for-a-pull-request-review-comment
      return ProxyRequest(request, cancellationToken);
    }

    [HttpDelete]
    [Route("reactions/{reactionId:long}")]
    public async Task<HttpResponseMessage> DeleteReaction(HttpRequestMessage request, CancellationToken cancellationToken, long reactionId) {
      // https://developer.github.com/v3/reactions/#delete-a-reaction
      var response = await ProxyRequest(request, cancellationToken);

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

    // ////////////////////////////////////////////////////////////
    // Issues
    // ////////////////////////////////////////////////////////////

    private async Task<HttpResponseMessage> ProxyIssue(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      var response = await ProxyRequest(request, cancellationToken);

      if (response.IsSuccessStatusCode) {
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
        } catch (Exception e) {
          // swallow db exceptions, since if we're here github has created the resource.
          // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/issues")]
    public Task<HttpResponseMessage> IssueCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/issues/#create-an-issue
      return ProxyIssue(request, cancellationToken, owner, repo);
    }

    [HttpPatch]
    [Route("repos/{owner}/{repo}/issues/{number:int}")]
    public Task<HttpResponseMessage> IssueEdit(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/issues/#edit-an-issue
      return ProxyIssue(request, cancellationToken, owner, repo);
    }

    [HttpPost]
    [HttpDelete]
    [Route("repos/{owner}/{repo}/issues/{number:int}/assignees")]
    public Task<HttpResponseMessage> IssueAssignees(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/issues/assignees/#add-assignees-to-an-issue
      // https://developer.github.com/v3/issues/assignees/#remove-assignees-from-an-issue
      return ProxyIssue(request, cancellationToken, owner, repo);
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/issues/{number:int}/labels")]
    public Task<HttpResponseMessage> IssueLabelsAdd(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/issues/labels/#add-labels-to-an-issue
      // TODO: Handle
      return ProxyRequest(request, cancellationToken);
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/issues/{number:int}/labels/{name}")]
    public Task<HttpResponseMessage> IssueLabelsRemove(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/issues/labels/#remove-a-label-from-an-issue
      // TODO: Handle
      return ProxyRequest(request, cancellationToken);
    }

    [HttpPut]
    [HttpDelete]
    [Route("repos/{owner}/{repo}/issues/{number:int}/lock")]
    public Task<HttpResponseMessage> IssueLock(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/issues/#lock-an-issue
      // https://developer.github.com/v3/issues/#unlock-an-issue
      return ProxyRequest(request, cancellationToken);
    }

    // ////////////////////////////////////////////////////////////
    // Issue Comments
    // ////////////////////////////////////////////////////////////

    private async Task<HttpResponseMessage> ProxyIssueComment(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      var response = await ProxyRequest(request, cancellationToken);

      if (response.IsSuccessStatusCode) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var comment = await response.Content.ReadAsAsync<IssueComment>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

          using (var context = new dm.ShipHubContext()) {
            var updater = new DataUpdater(context, _mapper);
            var repoName = $"{owner}/{repo}";
            var repoId = await context.Repositories
              .AsNoTracking()
              .Where(x => x.FullName == repoName)
              .Select(x => x.Id)
              .SingleAsync();

            await updater.UpdateIssueComments(repoId, response.Headers.Date ?? DateTimeOffset.UtcNow, new[] { comment });
            await updater.Changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          // swallow db exceptions, since if we're here github has created the resource.
          // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/issues/{number:int}/comments")]
    public Task<HttpResponseMessage> IssueCommentCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/issues/comments/#create-a-comment
      return ProxyIssueComment(request, cancellationToken, owner, repo);
    }

    [HttpPatch]
    [Route("repos/{owner}/{repo}/issues/comments/{id:long}")]
    public Task<HttpResponseMessage> IssueCommentEdit(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/issues/comments/#edit-a-comment
      return ProxyIssueComment(request, cancellationToken, owner, repo);
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/issues/comments/{commentId:long}")]
    public async Task<HttpResponseMessage> IssueCommentDelete(HttpRequestMessage request, CancellationToken cancellationToken, long commentId) {
      // https://developer.github.com/v3/issues/comments/#delete-a-comment
      var response = await ProxyRequest(request, cancellationToken);

      if (response.StatusCode == HttpStatusCode.NoContent) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          using (var context = new dm.ShipHubContext()) {
            var changes = await context.DeleteIssueComment(commentId, null);
            await changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    // ////////////////////////////////////////////////////////////
    // Issue Labels
    // ////////////////////////////////////////////////////////////

    private async Task<HttpResponseMessage> ProxyLabel(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      var response = await ProxyRequest(request, cancellationToken);

      if (response.IsSuccessStatusCode) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var label = await response.Content.ReadAsAsync<Label>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

          using (var context = new dm.ShipHubContext()) {
            var updater = new DataUpdater(context, _mapper);
            var repoName = $"{owner}/{repo}";
            var repoId = await context.Repositories
              .AsNoTracking()
              .Where(x => x.FullName == repoName)
              .Select(x => x.Id)
              .SingleAsync();

            await updater.UpdateLabels(repoId, new[] { label });
            await updater.Changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          // swallow db exceptions, since if we're here github has created the resource.
          // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/labels")]
    public Task<HttpResponseMessage> IssueLabelCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/issues/labels/#create-a-label
      return ProxyLabel(request, cancellationToken, owner, repo);
    }

    [HttpPatch]
    [Route("repos/{owner}/{repo}/labels/{name}")]
    public Task<HttpResponseMessage> IssueLabelEdit(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/issues/labels/#update-a-label
      return ProxyLabel(request, cancellationToken, owner, repo);
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/labels/{name}")]
    public async Task<HttpResponseMessage> IssueLabelDelete(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, string name) {
      // https://developer.github.com/v3/issues/labels/#delete-a-label
      var response = await ProxyRequest(request, cancellationToken);

      if (response.StatusCode == HttpStatusCode.NoContent) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          using (var context = new dm.ShipHubContext()) {
            // Eww
            var repoFullName = $"{owner}/{repo}";
            var labelId = await context.Labels.AsNoTracking()
              .Where(x => x.Repository.FullName == repoFullName)
              .Where(x => x.Name == name)
              .Select(x => (long?)x.Id)
              .SingleOrDefaultAsync();

            if (labelId != null) {
              var changes = await context.DeleteLabel(labelId.Value);
              await changes.Submit(_queueClient);
            }
          }
        } catch (Exception e) {
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    // ////////////////////////////////////////////////////////////
    // Issue Milestones
    // ////////////////////////////////////////////////////////////

    private async Task<HttpResponseMessage> ProxyMilestone(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      var response = await ProxyRequest(request, cancellationToken);

      if (response.IsSuccessStatusCode) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var milestone = await response.Content.ReadAsAsync<Milestone>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

          using (var context = new dm.ShipHubContext()) {
            var updater = new DataUpdater(context, _mapper);
            var repoName = $"{owner}/{repo}";
            var repoId = await context.Repositories
              .AsNoTracking()
              .Where(x => x.FullName == repoName)
              .Select(x => x.Id)
              .SingleAsync();

            await updater.UpdateMilestones(repoId, response.Headers.Date ?? DateTimeOffset.UtcNow, new[] { milestone });
            await updater.Changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          // swallow db exceptions, since if we're here github has created the resource.
          // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/milestones")]
    public Task<HttpResponseMessage> IssueMilestoneCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/issues/milestones/#create-a-milestone
      return ProxyMilestone(request, cancellationToken, owner, repo);
    }

    [HttpPatch]
    [Route("repos/{owner}/{repo}/milestones/{number:long}")]
    public Task<HttpResponseMessage> IssueMilestoneEdit(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/issues/milestones/#update-a-milestone
      return ProxyMilestone(request, cancellationToken, owner, repo);
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/milestones/{number:long}")]
    public async Task<HttpResponseMessage> IssueMilestoneDelete(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, long number) {
      var response = await ProxyRequest(request, cancellationToken);

      if (response.StatusCode == HttpStatusCode.NoContent) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          using (var context = new dm.ShipHubContext()) {
            // Eww
            var repoFullName = $"{owner}/{repo}";
            var milestoneId = await context.Milestones.AsNoTracking()
              .Where(x => x.Repository.FullName == repoFullName)
              .Where(x => x.Number == number)
              .Select(x => (long?)x.Id)
              .SingleOrDefaultAsync();

            if (milestoneId != null) {
              var changes = await context.DeleteMilestone(milestoneId.Value);
              await changes.Submit(_queueClient);
            }
          }
        } catch (Exception e) {
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    // ////////////////////////////////////////////////////////////
    // Projects
    // ////////////////////////////////////////////////////////////

    // TODO: Do we use these?

    // ////////////////////////////////////////////////////////////
    // Pull Requests
    // ////////////////////////////////////////////////////////////

    private async Task<HttpResponseMessage> ProxyPullRequest(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      var response = await ProxyRequest(request, cancellationToken);

      if (response.IsSuccessStatusCode) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var pr = await response.Content.ReadAsAsync<PullRequest>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

          using (var context = new dm.ShipHubContext()) {
            var updater = new DataUpdater(context, _mapper);
            var repoName = $"{owner}/{repo}";
            var repoId = await context.Repositories
              .AsNoTracking()
              .Where(x => x.FullName == repoName)
              .Select(x => x.Id)
              .SingleAsync();

            await updater.UpdatePullRequests(repoId, response.Headers.Date ?? DateTimeOffset.UtcNow, new[] { pr });
            await updater.Changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          // swallow db exceptions, since if we're here github has created the resource.
          // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/pulls")]
    public Task<HttpResponseMessage> PullRequestCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/pulls/#create-a-pull-request
      // TODO: This doesn't actually work. PRs saved before the associated issue are dropped in the DB. Oops.
      return ProxyPullRequest(request, cancellationToken, owner, repo);
    }

    [HttpPatch]
    [Route("repos/{owner}/{repo}/pulls/{number:int}")]
    public Task<HttpResponseMessage> PullRequestEdit(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/pulls/#update-a-pull-request
      return ProxyPullRequest(request, cancellationToken, owner, repo);
    }

    [HttpPut]
    [Route("repos/{owner}/{repo}/pulls/{number:int}/merge")]
    public Task<HttpResponseMessage> PullRequestMerge(HttpRequestMessage request, CancellationToken cancellationToken) {
      // https://developer.github.com/v3/pulls/#merge-a-pull-request-merge-button
      // TODO: Actually do something here
      return ProxyRequest(request, cancellationToken);
    }

    // ////////////////////////////////////////////////////////////
    // Pull Request Reviews
    // ////////////////////////////////////////////////////////////

    private async Task<HttpResponseMessage> ProxyPullRequestReview(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, int number) {
      var response = await ProxyRequest(request, cancellationToken);

      if (response.IsSuccessStatusCode) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var review = await response.Content.ReadAsAsync<Review>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

          using (var context = new dm.ShipHubContext()) {
            var updater = new DataUpdater(context, _mapper);
            var repoName = $"{owner}/{repo}";
            var ids = await context.Issues
              .AsNoTracking()
              .Where(x => x.Repository.FullName == repoName)
              .Where(x => x.Number == number)
              .Select(x => new { IssueId = x.Id, RepositoryId = x.RepositoryId})
              .SingleAsync();

            await updater.UpdateReviews(ids.RepositoryId, ids.IssueId, response.Headers.Date ?? DateTimeOffset.UtcNow, new[] { review }, user.UserId);
            await updater.Changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          // swallow db exceptions, since if we're here github has created the resource.
          // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/pulls/{issueNumber:int}/reviews")]
    public Task<HttpResponseMessage> PullRequestReviewCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, int issueNumber) {
      // https://developer.github.com/v3/pulls/reviews/#create-a-pull-request-review
      return ProxyPullRequestReview(request, cancellationToken, owner, repo, issueNumber);
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/pulls/{issueNumber:int}/reviews/{reviewId:long}/events")]
    public Task<HttpResponseMessage> PullRequestReviewSubmit(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, int issueNumber) {
      // https://developer.github.com/v3/pulls/reviews/#submit-a-pull-request-review
      return ProxyPullRequestReview(request, cancellationToken, owner, repo, issueNumber);
    }

    [HttpPut]
    [Route("repos/{owner}/{repo}/pulls/{issueNumber:int}/reviews/{reviewId:long}/dismissals")]
    public Task<HttpResponseMessage> PullRequestReviewDismiss(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, int issueNumber) {
      // https://developer.github.com/v3/pulls/reviews/#dismiss-a-pull-request-review
      return ProxyPullRequestReview(request, cancellationToken, owner, repo, issueNumber);
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/pulls/{issueNumber:int}/reviews/{reviewId:long}")]
    public async Task<HttpResponseMessage> PullRequestReviewDelete(HttpRequestMessage request, CancellationToken cancellationToken, long reviewId) {
      // https://developer.github.com/v3/pulls/reviews/#delete-a-pending-review
      var response = await ProxyRequest(request, cancellationToken);

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

    // ////////////////////////////////////////////////////////////
    // Pull Request Review Comments
    // ////////////////////////////////////////////////////////////

    [HttpPost]
    [Route("repos/{owner}/{repo}/pulls/{issueNumber:int}/comments")]
    public async Task<HttpResponseMessage> PullRequestReviewCommentCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, int issueNumber) {
      // https://developer.github.com/v3/pulls/comments/#create-a-comment
      var response = await ProxyRequest(request, cancellationToken);

      if (response.IsSuccessStatusCode) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var comment = await response.Content.ReadAsAsync<PullRequestComment>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

          using (var context = new dm.ShipHubContext()) {
            var updater = new DataUpdater(context, _mapper);
            var repoName = $"{owner}/{repo}";
            var ids = await context.PullRequests
              .AsNoTracking()
              .Where(x => x.Repository.FullName == repoName)
              .Where(x => x.Number == issueNumber)
              .Select(x => new { IssueId = x.IssueId, RepositoryId = x.RepositoryId })
              .SingleAsync();

            await updater.UpdatePullRequestComments(ids.RepositoryId, ids.IssueId, response.Headers.Date ?? DateTimeOffset.UtcNow, new[] { comment });
            await updater.Changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          // swallow db exceptions, since if we're here github has created the resource.
          // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpPatch]
    [Route("repos/{owner}/{repo}/pulls/comments/{commentId:long}")]
    public async Task<HttpResponseMessage> PullRequestReviewCommentEdit(HttpRequestMessage request, CancellationToken cancellationToken, long commentId) {
      // https://developer.github.com/v3/pulls/comments/#edit-a-comment
      var response = await ProxyRequest(request, cancellationToken);

      if (response.IsSuccessStatusCode) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var comment = await response.Content.ReadAsAsync<PullRequestComment>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

          using (var context = new dm.ShipHubContext()) {
            var updater = new DataUpdater(context, _mapper);
            var ids = await context.PullRequestComments
              .AsNoTracking()
              .Where(x => x.Id == commentId)
              .Select(x => new { IssueId = x.IssueId, RepositoryId = x.RepositoryId })
              .SingleOrDefaultAsync();

            if (ids != null) {
              await updater.UpdatePullRequestComments(ids.RepositoryId, ids.IssueId, response.Headers.Date ?? DateTimeOffset.UtcNow, new[] { comment });
              await updater.Changes.Submit(_queueClient);
            }
          }
        } catch (Exception e) {
          // swallow db exceptions, since if we're here github has created the resource.
          // we'll probably get it fixed in our db sooner or later, but for now we need to give the client its data.
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/pulls/comments/{commentId:long}")]
    public async Task<HttpResponseMessage> PullRequestReviewCommentDelete(HttpRequestMessage request, CancellationToken cancellationToken, long commentId) {
      // https://developer.github.com/v3/pulls/comments/#delete-a-comment
      var response = await ProxyRequest(request, cancellationToken);

      if (response.StatusCode == HttpStatusCode.NoContent) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          using (var context = new dm.ShipHubContext()) {
            var changes = await context.DeletePullRequestComment(commentId, null);
            await changes.Submit(_queueClient);
          }
        } catch (Exception e) {
          e.Report($"request: {request.RequestUri} response: {response} user: {user.DebugIdentifier}", user.DebugIdentifier);
        }
      }

      return response;
    }

    // ////////////////////////////////////////////////////////////
    // Pull Request Reviewers
    // ////////////////////////////////////////////////////////////

    [HttpPost]
    [Route("repos/{owner}/{repo}/pulls/{issueNumber:int}/requested_reviewers")]
    public Task<HttpResponseMessage> ReviewRequestCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      // https://developer.github.com/v3/pulls/review_requests/#create-a-review-request
      return ProxyPullRequest(request, cancellationToken, owner, repo);
    }

    [HttpDelete]
    [Route("repos/{owner}/{repo}/pulls/{issueNumber:int}/requested_reviewers")]
    public async Task<HttpResponseMessage> DeleteRequestedReviewer(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo, int issueNumber) {
      // https://developer.github.com/v3/pulls/review_requests/#delete-a-review-request
      var response = await ProxyRequest(request, cancellationToken);

      if (response.StatusCode == HttpStatusCode.OK) {
        var user = RequestContext.Principal as ShipHubPrincipal;
        try {
          await response.Content.LoadIntoBufferAsync();
          var data = await response.Content.ReadAsAsync<JToken>(GitHubSerialization.MediaTypeFormatters, cancellationToken);
          var removed = data.Value<IEnumerable<string>>("reviewers");

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
  }
}
