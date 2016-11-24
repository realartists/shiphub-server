namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
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
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Newtonsoft.Json;
  using Orleans;
  using QueueClient;

  [AllowAnonymous]
  public class GitHubWebhookController : ShipHubController {
    public const string GitHubUserAgent = "GitHub-Hookshot";
    public const string EventHeaderName = "X-GitHub-Event";
    public const string DeliveryIdHeaderName = "X-GitHub-Delivery";
    public const string SignatureHeaderName = "X-Hub-Signature";

    private IGrainFactory _grainFactory;
    private IShipHubQueueClient _queueClient;
    private IMapper _mapper;

    public GitHubWebhookController(IShipHubQueueClient queueClient, IMapper mapper, IGrainFactory grainFactory) {
      _grainFactory = grainFactory;
      _queueClient = queueClient;
      _mapper = mapper;
    }

    private async Task<T> ReadPayloadAsync<T>(Hook hook) {
      var signatureHeader = Request.Headers.GetValues(SignatureHeaderName).Single();
      byte[] signature = SoapHexBinary.Parse(signatureHeader.Substring(5)).Value; // header of form "sha1=..."

      using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(hook.Secret.ToString())))
      using (var bodyStream = await Request.Content.ReadAsStreamAsync())
      using (var hmacStream = new CryptoStream(bodyStream, hmac, CryptoStreamMode.Read))
      using (var textReader = new StreamReader(hmacStream, Encoding.UTF8))
      using (var jsonReader = new JsonTextReader(textReader) { CloseInput = true }) {
        var payload = GitHubSerialization.JsonSerializer.Deserialize<T>(jsonReader);
        jsonReader.Close();
        // We're not worth launching a timing attack against.
        if (!signature.SequenceEqual(hmac.Hash)) {
          throw new HttpResponseException(HttpStatusCode.BadRequest);
        }
        return payload;
      }
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("webhook/{type:regex(^(org|repo)$)}/{id:long}")]
    public async Task<IHttpActionResult> HandleHook(string type, long id) {
      if (Request.Headers.UserAgent.Single().Product.Name != GitHubUserAgent) {
        return BadRequest("Not you.");
      }

      var eventName = Request.Headers.GetValues(EventHeaderName).Single();
      var deliveryIdHeader = Request.Headers.GetValues(DeliveryIdHeaderName).Single();
      var deliveryId = Guid.Parse(deliveryIdHeader);

      Hook hook = null;

      if (type == "org") {
        hook = await Context.Hooks.SingleOrDefaultAsync(x => x.OrganizationId == id);
      } else if (type == "repo") {
        hook = await Context.Hooks.SingleOrDefaultAsync(x => x.RepositoryId == id);
      }

      if (hook == null) {
        // I don't care anymore. This is GitHub's problem.
        // They should support unsubscribing from a hook with a special response code or body.
        // We may not even have credentials to remove the hook anymore.
        return NotFound();
      }

      ChangeSummary changeSummary = null;

      switch (eventName) {
        case "issues": {
            var payload = await ReadPayloadAsync<WebhookIssuePayload>(hook);
            switch (payload.Action) {
              case "opened":
              case "closed":
              case "reopened":
              case "edited":
              case "labeled":
              case "unlabeled":
              case "assigned":
              case "unassigned":
              case "milestoned":
              case "demilestoned":
                changeSummary = await HandleIssues(payload);
                break;
            }
            break;
          }
        case "issue_comment": {
            var payload = await ReadPayloadAsync<WebhookIssuePayload>(hook);
            switch (payload.Action) {
              case "created":
              case "edited":
              case "deleted":
                changeSummary = await HandleIssueComment(payload);
                break;
            }
            break;
          }
        case "milestone": {
            var payload = await ReadPayloadAsync<WebhookIssuePayload>(hook);
            switch (payload.Action) {
              case "closed":
              case "created":
              case "deleted":
              case "edited":
              case "opened":
                changeSummary = await HandleMilestone(payload);
                break;
            }
            break;
          }
        case "label": {
            var payload = await ReadPayloadAsync<WebhookIssuePayload>(hook);
            switch (payload.Action) {
              case "created":
              case "edited":
              case "deleted":
                changeSummary = await HandleLabel(payload);
                break;
            }
            break;
          }
        case "repository": {
            var payload = await ReadPayloadAsync<WebhookIssuePayload>(hook);
            if (
            // Created events can only come from the org-level hook.
            payload.Action == "created" ||
            // We'll get deletion events from both the repo and org, but
            // we'll ignore the org one.
            (type == "repo" && payload.Action == "deleted")) {
              await HandleRepository(payload);
            }
            break;
          }
        case "push": {
            var payload = await ReadPayloadAsync<WebhookPushPayload>(hook);
            await HandlePush(payload);
            break;
          }
        case "ping":
          await ReadPayloadAsync<object>(hook); // read payload to validate signature
          break;
        default:
          throw new NotImplementedException($"Webhook event '{eventName}' is not handled. Either support it or don't subscribe to it.");
      }

      hook.LastSeen = DateTimeOffset.UtcNow;
      // Reset the ping count so this webhook won't get reaped.
      hook.PingCount = null;
      hook.LastPing = null;
      await Context.SaveChangesAsync();

      if (_queueClient != null && changeSummary != null && !changeSummary.Empty) {
        await _queueClient.NotifyChanges(changeSummary);
      }

      return StatusCode(HttpStatusCode.Accepted);
    }

    private async Task<ChangeSummary> HandleIssueComment(WebhookIssuePayload payload) {
      // Ensure the issue that owns this comment exists locally before we add the comment.
      var summary = await HandleIssues(payload);

      using (var context = new ShipHubContext()) {
        if (payload.Action == "deleted") {
          summary.UnionWith(await context.DeleteComments(new[] { payload.Comment.Id }));
        } else {
          summary.UnionWith(await context.BulkUpdateAccounts(
          DateTimeOffset.UtcNow,
          _mapper.Map<IEnumerable<AccountTableType>>(new[] { payload.Comment.User })));

          summary.UnionWith(await context.BulkUpdateComments(
            payload.Repository.Id,
            _mapper.Map<IEnumerable<CommentTableType>>(new[] { payload.Comment })));
        }
      }

      return summary;
    }

    private async Task HandleRepository(WebhookIssuePayload payload) {
      if (payload.Repository.Owner.Type == GitHubAccountType.Organization) {
        var users = await Context.OrganizationAccounts
          .Where(x => x.OrganizationId == payload.Repository.Owner.Id)
          .Where(x => x.User.Token != null)
          .Select(x => x.User)
          .ToListAsync();

        await Task.WhenAll(
          users.Select(x => {
            var userActor = _grainFactory.GetGrain<IUserActor>(x.Id);
            return userActor.ForceSyncRepositories();
          })
        );
      } else {
        // TODO: This should also trigger a sync for contributors of a repo, but at
        // least this is more correct than what we have now.
        var owner = await Context.Accounts.SingleOrDefaultAsync(x => x.Id == payload.Repository.Owner.Id);
        if (owner.Token != null) {
          var userActor = _grainFactory.GetGrain<IUserActor>(owner.Id);
          await userActor.ForceSyncRepositories();
        }
      }
    }

    private async Task<ChangeSummary> HandleIssues(WebhookIssuePayload payload) {
      var summary = new ChangeSummary();
      if (payload.Issue.Milestone != null) {
        var milestone = _mapper.Map<MilestoneTableType>(payload.Issue.Milestone);
        var milestoneSummary = await Context.BulkUpdateMilestones(
          payload.Repository.Id,
          new MilestoneTableType[] { milestone });
        summary.UnionWith(milestoneSummary);
      }

      var referencedAccounts = new List<Common.GitHub.Models.Account>();
      referencedAccounts.Add(payload.Issue.User);
      if (payload.Issue.Assignees != null) {
        referencedAccounts.AddRange(payload.Issue.Assignees);
      }
      if (payload.Issue.ClosedBy != null) {
        referencedAccounts.Add(payload.Issue.ClosedBy);
      }

      if (referencedAccounts.Count > 0) {
        var accountsMapped = _mapper.Map<IEnumerable<AccountTableType>>(referencedAccounts.Distinct(x => x.Id));
        summary.UnionWith(await Context.BulkUpdateAccounts(DateTimeOffset.UtcNow, accountsMapped));
      }

      var issues = new List<Common.GitHub.Models.Issue> { payload.Issue };
      var issuesMapped = _mapper.Map<IEnumerable<IssueTableType>>(issues);

      var labels = payload.Issue.Labels?.Select(x => new LabelTableType() {
        Id = x.Id,
        Color = x.Color,
        Name = x.Name,
      });

      var assigneeMappings = payload.Issue.Assignees?.Select(x => new MappingTableType() {
        Item1 = payload.Issue.Id,
        Item2 = x.Id,
      });

      summary.UnionWith(
        await Context.BulkUpdateLabels(payload.Repository.Id, labels),
        await Context.BulkUpdateIssues(
          payload.Repository.Id,
          issuesMapped,
          payload.Issue.Labels?.Select(x => new MappingTableType() { Item1 = payload.Issue.Id, Item2 = x.Id }),
          assigneeMappings));

      return summary;
    }

    private async Task<ChangeSummary> HandleMilestone(WebhookIssuePayload payload) {
      if (payload.Action == "deleted") {
        var summary = await Context.DeleteMilestone(payload.Milestone.Id);
        return summary;
      } else {
        var summary = await Context.BulkUpdateMilestones(
          payload.Repository.Id,
          new[] { _mapper.Map<MilestoneTableType>(payload.Milestone) });
        return summary;
      }
    }

    private Task HandlePush(WebhookPushPayload payload) {
      var defaultBranch = payload.Repository.DefaultBranch ?? "";
      var defaultRef = $"refs/heads/{defaultBranch}";
      if (defaultRef != payload.Ref) {
        return Task.CompletedTask; // if not a push to default branch then we don't care
      }

      bool hasIssueTemplate;
      if (payload.Size > payload.Commits.Count()) {
        hasIssueTemplate = true; // if we can't enumerate all the commits, assume ISSUE_TEMPLATE was changed.
      } else {
        // the payload includes all of the commits. take a look and see if we can find the ISSUE_TEMPLATE.md in any of the edited files
        hasIssueTemplate = payload.Commits
          .Aggregate(new HashSet<string>(), (accum, commit) => {
            accum.UnionWith(commit.Added);
            accum.UnionWith(commit.Modified);
            accum.UnionWith(commit.Removed);
            return accum;
          })
          .Any(f => GitHubClient.IssueTemplateRegex.IsMatch(Path.GetFileName(f)));
      }

      if (hasIssueTemplate) {
        var repoActor = _grainFactory.GetGrain<IRepositoryActor>(payload.Repository.Id);
        return repoActor.SyncIssueTemplate();
      } else {
        return Task.CompletedTask;
      }
    }

    private async Task<ChangeSummary> HandleLabel(WebhookIssuePayload payload) {
      switch (payload.Action) {
        case "created":
        case "edited":
          return await Context.BulkUpdateLabels(
          payload.Repository.Id,
          new[] {
            new LabelTableType() {
              Id = payload.Label.Id,
              Name = payload.Label.Name,
              Color = payload.Label.Color,
            },
          });
        case "deleted":
          return await Context.DeleteLabel(payload.Label.Id);
        default:
          throw new ArgumentException("Unexpected action: " + payload.Action);
      }
    }
  }
}
