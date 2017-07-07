namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Data.Entity;
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

    public const int GrainSprayWidth = 256;

    private IAsyncGrainFactory _grainFactory;

    public GitHubWebhookController(IAsyncGrainFactory grainFactory) {
      _grainFactory = grainFactory;
    }

    private async Task<T> ReadPayloadAsync<T>(byte[] signature, byte[] secret) {
      using (var hmac = new HMACSHA1(secret))
      using (var bodyStream = await Request.Content.ReadAsStreamAsync())
      using (var hmacStream = new CryptoStream(bodyStream, hmac, CryptoStreamMode.Read))
      using (var textReader = new StreamReader(hmacStream, Encoding.UTF8))
      using (var jsonReader = new JsonTextReader(textReader) { CloseInput = true }) {
        var payload = GitHubSerialization.JsonSerializer.Deserialize<T>(jsonReader);
        jsonReader.Close();
        // We're not worth launching a timing attack against.
        if (!hmac.Hash.SequenceEqual(signature)) {
          throw new HttpResponseException(HttpStatusCode.BadRequest);
        }
        return payload;
      }
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("webhook/{type:regex(^(org|repo)$)}/{id:long}")]
    public async Task<IHttpActionResult> ReceiveHook(string type, long id) {
      if (Request.Headers.UserAgent.Single().Product.Name != GitHubUserAgent) {
        return BadRequest("Not you.");
      }

      // Invalid inputs can make these fail. That's ok.
      var eventName = Request.ParseHeader(EventHeaderName, x => x);
      var deliveryId = Request.ParseHeader(DeliveryIdHeaderName, x => Guid.Parse(x));

      // signature of the form "sha1=..."
      var signature = Request.ParseHeader(SignatureHeaderName, x => SoapHexBinary.Parse(x.Substring(5)).Value);

      using (var context = new ShipHubContext()) {
        Hook hook;
        if (type == "org") {
          hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == id);
        } else {
          hook = await context.Hooks.AsNoTracking().SingleOrDefaultAsync(x => x.RepositoryId == id);
        }

        if (hook == null) {
          // I don't care anymore. This is GitHub's problem.
          // They should support unsubscribing from a hook with a special response code or body.
          // We may not even have credentials to remove the hook anymore.
          return NotFound();
        }

        var secret = Encoding.UTF8.GetBytes(hook.Secret.ToString());
        var webhookEventActor = await _grainFactory.GetGrain<IWebhookEventActor>(0); // Stateless worker grain with single pool (0)
        var debugInfo = $"[{type}:{id}#{eventName}/{deliveryId}]";

        Task hookTask = null;
        switch (eventName) {
          case "commit_comment": {
              var payload = await ReadPayloadAsync<CommitCommentPayload>(signature, secret);
              hookTask = webhookEventActor.CommitComment(DateTimeOffset.UtcNow, payload);
            }
            break;
          case "issue_comment": {
              var payload = await ReadPayloadAsync<IssueCommentPayload>(signature, secret);
              hookTask = webhookEventActor.IssueComment(DateTimeOffset.UtcNow, payload);
            }
            break;
          case "issues": {
              var payload = await ReadPayloadAsync<IssuesPayload>(signature, secret);
              hookTask = webhookEventActor.Issues(DateTimeOffset.UtcNow, payload);
            }
            break;
          case "label": {
              var payload = await ReadPayloadAsync<LabelPayload>(signature, secret);
              hookTask = webhookEventActor.Label(DateTimeOffset.UtcNow, payload);
            }
            break;
          case "milestone": {
              var payload = await ReadPayloadAsync<MilestonePayload>(signature, secret);
              hookTask = webhookEventActor.Milestone(DateTimeOffset.UtcNow, payload);
            }
            break;
          case "ping":
            await ReadPayloadAsync<object>(signature, secret); // read payload to validate signature
            break;
          case "pull_request_review_comment": {
              var payload = await ReadPayloadAsync<PullRequestReviewCommentPayload>(signature, secret);
              hookTask = webhookEventActor.PullRequestReviewComment(DateTimeOffset.UtcNow, payload);
            }
            break;
          case "pull_request_review": {
              var payload = await ReadPayloadAsync<PullRequestReviewPayload>(signature, secret);
              hookTask = webhookEventActor.PullRequestReview(DateTimeOffset.UtcNow, payload);
            }
            break;
          case "pull_request": {
              var payload = await ReadPayloadAsync<PullRequestPayload>(signature, secret);
              hookTask = webhookEventActor.PullRequest(DateTimeOffset.UtcNow, payload);
            }
            break;
          case "push": {
              var payload = await ReadPayloadAsync<PushPayload>(signature, secret);
              hookTask = webhookEventActor.Push(DateTimeOffset.UtcNow, payload);
              break;
            }
          case "repository": {
              var payload = await ReadPayloadAsync<RepositoryPayload>(signature, secret);
              hookTask = webhookEventActor.Repository(DateTimeOffset.UtcNow, payload);
              break;
            }
          case "status": {
              var payload = await ReadPayloadAsync<StatusPayload>(signature, secret);
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

        // Reset the ping count so this webhook won't get reaped.
        await context.BulkUpdateHooks(seen: new[] { hook.Id });
      }

      return StatusCode(HttpStatusCode.Accepted);
    }
  }
}
