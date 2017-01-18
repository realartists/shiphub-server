namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Security.Authentication;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces;
  using AutoMapper;
  using Common;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Filters;
  using Orleans;
  using QueueClient;
  using dm = Common.DataModel;

  [RoutePrefix("github")]
  public class GitHubProxyController : ApiController {
    // If you increase this, you may also need to update timeouts on the handler
    // For 60 seconds, the defaults are fine.
    private static readonly TimeSpan _ProxyTimeout = TimeSpan.FromSeconds(60);

    private static readonly WinHttpHandler _ProxyHandler = new WinHttpHandler() {
      AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
      AutomaticRedirection = true,
      CheckCertificateRevocationList = true,
      CookieUsePolicy = CookieUsePolicy.IgnoreCookies,
      MaxAutomaticRedirections = 3,
      SslProtocols = SslProtocols.Tls12,
      WindowsProxyUsePolicy = WindowsProxyUsePolicy.DoNotUseProxy,
      // The default timeout values are all longer than our overall timeout.
    };

    // Using one HttpClient for all requests should be safe according to the documentation.
    // See https://msdn.microsoft.com/en-us/library/system.net.http.httpclient(v=vs.110).aspx?f=255&mspperror=-2147217396#Anchor_5
    private static readonly HttpClient _ProxyClient = new HttpClient(_ProxyHandler) {
      MaxResponseContentBufferSize = 1024 * 1024 * 5, // 5MB is pretty generous
      Timeout = _ProxyTimeout
    };

    private static readonly HashSet<HttpMethod> _BareMethods = new HashSet<HttpMethod>() { HttpMethod.Delete, HttpMethod.Get, HttpMethod.Head, HttpMethod.Options };

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
      var user = RequestContext.Principal as ShipHubPrincipal;
      request.Headers.Authorization = new AuthenticationHeaderValue("token", user.Token);

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

          // Trigger issue event and comment sync.
          var issueGrain = _grainFactory.GetGrain<IIssueActor>(issue.Number, $"{owner}/{repo}", grainClassNamePrefix: null);
          issueGrain.SyncInteractive(user.UserId).LogFailure(user.DebugIdentifier);

          if (!changes.IsEmpty) {
            await _queueClient.NotifyChanges(changes);
          }
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
