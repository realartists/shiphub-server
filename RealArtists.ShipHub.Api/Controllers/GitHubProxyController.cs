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
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Filters;
  using QueueClient;
  using dm = Common.DataModel;

  [RoutePrefix("github")]
  public class GitHubProxyController : ApiController {
    // Using one HttpClient for all requests should be safe according to the documentation.
    // See https://msdn.microsoft.com/en-us/library/system.net.http.httpclient(v=vs.110).aspx?f=255&mspperror=-2147217396#Anchor_5
    private static readonly HttpClient _ProxyClient = new HttpClient() {
      MaxResponseContentBufferSize = 1024 * 1024 * 5, // 5MB is pretty generous
      Timeout = TimeSpan.FromSeconds(10), // so is 10 seconds
    };

    private static readonly HashSet<HttpMethod> _BareMethods = new HashSet<HttpMethod>() { HttpMethod.Delete, HttpMethod.Get, HttpMethod.Head, HttpMethod.Options };

    private IMapper _mapper;
    private IShipHubQueueClient _queueClient;

    public GitHubProxyController(IMapper mapper, IShipHubQueueClient queueClient) {
      _mapper = mapper;
      _queueClient = queueClient;
    }

    [HttpDelete]
    [HttpGet]
    [HttpHead]
    [HttpOptions]
    [HttpPatch]
    [HttpPost]
    [HttpPut]
    [Route("{*path}")]
    public async Task<HttpResponseMessage> ProxyBlind(HttpRequestMessage request, CancellationToken cancellationToken, string path) {
      var builder = new UriBuilder(request.RequestUri);
      builder.Scheme = Uri.UriSchemeHttps;
      builder.Port = 443;
      builder.Host = "api.github.com";
      builder.Path = path;
      request.RequestUri = builder.Uri;

      request.Headers.Host = request.RequestUri.Host;
      var user = RequestContext.Principal as ShipHubPrincipal;
      request.Headers.Authorization = new AuthenticationHeaderValue("token", user.Token);

      // This is dumb
      if (_BareMethods.Contains(request.Method)) {
        request.Content = null;
      }

      try {
        var response = await _ProxyClient.SendAsync(request, cancellationToken);
        response.Headers.Remove("Server");
        return response;
      } catch (TaskCanceledException exception) {
        return request.CreateErrorResponse(HttpStatusCode.GatewayTimeout, exception);
      }
    }

    [HttpPost]
    [Route("repos/{owner}/{repo}/issues")]
    public async Task<HttpResponseMessage> IssueCreate(HttpRequestMessage request, CancellationToken cancellationToken, string owner, string repo) {
      var builder = new UriBuilder(request.RequestUri);
      builder.Scheme = Uri.UriSchemeHttps;
      builder.Port = 443;
      builder.Host = "api.github.com";
      builder.Path = $"repos/{owner}/{repo}/issues";
      request.RequestUri = builder.Uri;

      request.Headers.Host = request.RequestUri.Host;
      var user = RequestContext.Principal as ShipHubPrincipal;
      request.Headers.Authorization = new AuthenticationHeaderValue("token", user.Token);

      // This is dumb
      if (_BareMethods.Contains(request.Method)) {
        request.Content = null;
      }

      try {
        var response = await _ProxyClient.SendAsync(request, cancellationToken);
        response.Headers.Remove("Server");

        // Process the response
        if (response.StatusCode == HttpStatusCode.Created) {
          await response.Content.LoadIntoBufferAsync();
          var issue = await response.Content.ReadAsAsync<Issue>(GitHubSerialization.MediaTypeFormatters, cancellationToken);

          // TODO: Unify this code with other issue update places to reduce bugs.

          var accounts = new[] { issue.User, issue.ClosedBy }
            .Concat(issue.Assignees)
            .Where(x => x != null)
            .Distinct(x => x.Id);

          ChangeSummary changes = null;
          var repoName = $"{owner}/{repo}";
          using (var context = new dm.ShipHubContext()) {
            var repoId = await context.Repositories
              .Where(x => x.FullName == repoName)
              .Select(x => x.Id)
              .SingleAsync();

            changes = await context.BulkUpdateAccounts(response.Headers.Date.Value, _mapper.Map<IEnumerable<AccountTableType>>(accounts));

            if (issue.Milestone != null) {
              changes.UnionWith(
                await context.BulkUpdateMilestones(repoId, _mapper.Map<IEnumerable<MilestoneTableType>>(new[] { issue.Milestone }))
              );
            }

            if (issue.Labels?.Count() > 0) {
              changes.UnionWith(await context.BulkUpdateLabels(
                repoId,
                issue.Labels?.Select(x => new LabelTableType() { Id = x.Id, Name = x.Name, Color = x.Color })));
            }

            changes.UnionWith(
              await context.BulkUpdateIssues(
              repoId,
              _mapper.Map<IEnumerable<IssueTableType>>(new[] { issue }),
              issue.Labels?.Select(y => new MappingTableType() { Item1 = issue.Id, Item2 = y.Id }),
              issue.Assignees?.Select(y => new MappingTableType() { Item1 = issue.Id, Item2 = y.Id }))
            );
          }

          if (!changes.Empty) {
            await _queueClient.NotifyChanges(changes);
          }
        }

        return response;
      } catch (TaskCanceledException exception) {
        return request.CreateErrorResponse(HttpStatusCode.GatewayTimeout, exception);
      }
    }
  }
}
