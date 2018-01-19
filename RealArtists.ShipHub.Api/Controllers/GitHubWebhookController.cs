namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Data.Entity;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Newtonsoft.Json;
  using RealArtists.ShipHub.Common.GitHub.Models.WebhookPayloads;

  [AllowAnonymous]
  public class GitHubWebhookController : ApiController {
    public const string GitHubUserAgent = "GitHub-Hookshot";
    public const string EventHeaderName = "X-GitHub-Event";
    public const string DeliveryIdHeaderName = "X-GitHub-Delivery";
    public const string SignatureHeaderName = "X-Hub-Signature";

    public const string HookTypeApp = "app";
    public const string HookTypeOrg = "org";
    public const string HookTypeRepo = "repo";

    public const int GrainSprayWidth = 256;

    private IAsyncGrainFactory _grainFactory;
    private IShipHubConfiguration _config;

    public GitHubWebhookController(IAsyncGrainFactory grainFactory, IShipHubConfiguration config) {
      _grainFactory = grainFactory;
      _config = config;
    }

    private async Task<T> ReadPayloadAsync<T>(byte[] signature, byte[] secret) {
      using (var hmac = new HMACSHA1(secret))
      using (var bodyStream = await Request.Content.ReadAsStreamAsync())
      using (var hmacStream = new CryptoStream(bodyStream, hmac, CryptoStreamMode.Read))
      using (var textReader = new StreamReader(hmacStream, Encoding.UTF8))
      using (var jsonReader = new JsonTextReader(textReader) { CloseInput = true }) {
        var payload = GitHubSerialization.JsonSerializer.Deserialize<T>(jsonReader);
        jsonReader.Close();

        // Constant time comparison (length not secret)
        var fail = 1;
        if (hmac.Hash.Length == signature.Length) {
          fail = 0;
          for (var i = 0; i < hmac.Hash.Length; ++i) {
            fail |= hmac.Hash[i] ^ signature[i];
          }
        }

        if (fail != 0) {
          Log.Info($"Invalid signature detected: {GetIPAddress()} {Request.RequestUri}");
          throw new HttpResponseException(HttpStatusCode.BadRequest);
        }

        return payload;
      }
    }

    private const string HttpContextKey = "MS_HttpContext";
    public const string RemoteEndpointMessageKey = "System.ServiceModel.Channels.RemoteEndpointMessageProperty";

    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    private string GetIPAddress() {
      try {
        if (Request.Properties.ContainsKey(HttpContextKey)) {
          dynamic ctx = Request.Properties[HttpContextKey];
          if (ctx != null) {
            return ctx.Request.UserHostAddress;
          }
        }

        if (Request.Properties.ContainsKey(RemoteEndpointMessageKey)) {
          dynamic remoteEndpoint = Request.Properties[RemoteEndpointMessageKey];
          if (remoteEndpoint != null) {
            return remoteEndpoint.Address;
          }
        }
      } catch (Exception ex) {
        ex.Report("Failed to determine client IP address.");
      }
      return null;
    }

    // Current hook IPs from https://api.github.com/meta are "192.30.252.0/22", "185.199.108.0/22"
    // This is a super gross hack.
    private static readonly uint HookNet1 = BitConverter.ToUInt32(IPAddress.Parse("192.30.252.0").GetAddressBytes().Reverse().ToArray(), 0) >> 10;
    private static readonly uint HookNet2 = BitConverter.ToUInt32(IPAddress.Parse("185.199.108.0").GetAddressBytes().Reverse().ToArray(), 0) >> 10;
    private static readonly IPAddress VpnIp = IPAddress.Parse("172.27.175.1");

    private bool IsRequestFromGitHub() {
      if (Request.Headers.UserAgent.Single().Product.Name != GitHubUserAgent) { return false; }

      var remoteIPString = GetIPAddress();
      if (IPAddress.TryParse(remoteIPString, out var remoteIP)) {
        if (remoteIP.Equals(VpnIp)) {
          return true;
        } else {
          var remoteIPBytes = remoteIP.GetAddressBytes();
          Array.Reverse(remoteIPBytes);
          var remoteIPInt = BitConverter.ToUInt32(remoteIPBytes, 0);
          var shifted = remoteIPInt >> 10;
          return shifted == HookNet1 || shifted == HookNet2;
        }
      }

      return false;
    }

    /// <summary>
    /// For broswer requests.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("webhook")]
    [Route("webhook/{type:regex(^(org|repo)$)}/{id:long}")]
    public IHttpActionResult RedirectBrowser() {
      return Redirect("https://www.realartists.com/docs/2.0/privacy.html");
    }

    /// <summary>
    /// For GitHub App hooks.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("github-app/hook")]
    public Task<IHttpActionResult> ReceiveAppHook() {
      return HandleHook(HookTypeApp);
    }

    /// <summary>
    /// For standard webhooks.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [Route("webhook/{type:regex(^(org|repo)$)}/{id:long}")]
    public Task<IHttpActionResult> ReceiveHook(string type, long id) {
      return HandleHook(type, id);
    }

    private async Task<IHttpActionResult> HandleHook(string type = null, long id = 0) {
      if (!IsRequestFromGitHub()) {
        Log.Info($"Rejecting webhook request from impersonator: {GetIPAddress()} {Request.RequestUri}");
        return BadRequest("Not you.");
      }

      // Invalid inputs can make these fail. That's ok.
      var eventName = Request.ParseHeader(EventHeaderName, x => x);
      var deliveryId = Request.ParseHeader(DeliveryIdHeaderName, x => Guid.Parse(x));

      // signature of the form "sha1=..."
      var signature = Request.ParseHeader(SignatureHeaderName, x => SoapHexBinary.Parse(x.Substring(5)).Value);

      string secret;
      long? hookId = null;
      if (type == HookTypeApp) {
        // There's only one app (for now) so we don't need the ID.
        // Lookup and validate the secret from app configuration.
        // This is safe because org/repo admins cannot see the secret.
        secret = _config.GitHubAppWebhookSecret;
      } else {
        using (var context = new ShipHubContext()) {
          Hook hook = null;

          if (type == HookTypeOrg) {
            hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == id);
          } else if (type == HookTypeRepo) {
            hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.RepositoryId == id);
          }

          secret = hook?.Secret.ToString();
          hookId = hook?.Id;
        }
      }

      if (string.IsNullOrWhiteSpace(secret)) {
        // I don't care anymore. This is GitHub's problem.
        // They should support unsubscribing from a hook with a special response code or body.
        // We may not even have credentials to remove the hook anymore.
        return NotFound();
      }

      var secretBytes = Encoding.UTF8.GetBytes(secret);
      var webhookEventActor = await _grainFactory.GetGrain<IWebhookEventActor>(0); // Stateless worker grain with single pool (0)
      var debugInfo = $"[{type}:{id}#{eventName}/{deliveryId}]";

      Task hookTask = null;

      switch (eventName) {
        case "commit_comment": {
            var payload = await ReadPayloadAsync<CommitCommentPayload>(signature, secretBytes);
            hookTask = webhookEventActor.CommitComment(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "installation" when type == HookTypeApp: {
            var payload = await ReadPayloadAsync<InstallationPayload>(signature, secretBytes);
            hookTask = webhookEventActor.Installation(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "installation_repositories" when type == HookTypeApp: {
            var payload = await ReadPayloadAsync<InstallationRepositoriesPayload>(signature, secretBytes);
            hookTask = webhookEventActor.InstallationRepositories(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "issue_comment": {
            var payload = await ReadPayloadAsync<IssueCommentPayload>(signature, secretBytes);
            hookTask = webhookEventActor.IssueComment(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "issues": {
            var payload = await ReadPayloadAsync<IssuesPayload>(signature, secretBytes);
            hookTask = webhookEventActor.Issues(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "label": {
            var payload = await ReadPayloadAsync<LabelPayload>(signature, secretBytes);
            hookTask = webhookEventActor.Label(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "milestone": {
            var payload = await ReadPayloadAsync<MilestonePayload>(signature, secretBytes);
            hookTask = webhookEventActor.Milestone(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "ping":
          await ReadPayloadAsync<object>(signature, secretBytes); // read payload to validate signature
          break;
        case "pull_request_review_comment": {
            var payload = await ReadPayloadAsync<PullRequestReviewCommentPayload>(signature, secretBytes);
            hookTask = webhookEventActor.PullRequestReviewComment(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "pull_request_review": {
            var payload = await ReadPayloadAsync<PullRequestReviewPayload>(signature, secretBytes);
            hookTask = webhookEventActor.PullRequestReview(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "pull_request": {
            var payload = await ReadPayloadAsync<PullRequestPayload>(signature, secretBytes);
            hookTask = webhookEventActor.PullRequest(DateTimeOffset.UtcNow, payload);
          }
          break;
        case "push": {
            var payload = await ReadPayloadAsync<PushPayload>(signature, secretBytes);
            hookTask = webhookEventActor.Push(DateTimeOffset.UtcNow, payload);
            break;
          }
        case "repository": {
            var payload = await ReadPayloadAsync<RepositoryPayload>(signature, secretBytes);
            hookTask = webhookEventActor.Repository(DateTimeOffset.UtcNow, payload);
            break;
          }
        case "status": {
            var payload = await ReadPayloadAsync<StatusPayload>(signature, secretBytes);
            hookTask = webhookEventActor.Status(DateTimeOffset.UtcNow, payload);
            break;
          }
        default:
          Log.Error($"Webhook event '{eventName}' is not handled. Either support it or don't subscribe to it.");
          break;
      }

      // Just in case
      if (hookTask == null && eventName != "ping") {
        Log.Error($"Webhook event '{eventName}' does net set the {nameof(hookTask)}. Failures will be silent.");
      }

      hookTask?.LogFailure(debugInfo);

      // TODO: LAST SEEN FOR APP HOOKS

      if (hookId.HasValue) {
        using (var context = new ShipHubContext()) {
          // Reset the ping count so this webhook won't get reaped.
          await context.BulkUpdateHooks(seen: new[] { hookId.Value });
        }
      }

      return StatusCode(HttpStatusCode.Accepted);
    }
  }
}
