namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
  using QueueClient;

  [AllowAnonymous]
  public class GitHubWebhookController : ShipHubController {
    public const string GitHubUserAgent = "GitHub-Hookshot";
    public const string EventHeaderName = "X-GitHub-Event";
    public const string DeliveryIdHeaderName = "X-GitHub-Delivery";
    public const string SignatureHeaderName = "X-Hub-Signature";

    private IShipHubBusClient _busClient;

    public GitHubWebhookController() : this(new ShipHubBusClient()) {
    }

    public GitHubWebhookController(IShipHubBusClient busClient) {
      _busClient = busClient;
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
      var signatureHeader = Request.Headers.GetValues(SignatureHeaderName).Single();

      // header of form "sha1=..."
      byte[] signature = SoapHexBinary.Parse(signatureHeader.Substring(5)).Value;
      var deliveryId = Guid.Parse(deliveryIdHeader);

      var payloadString = await Request.Content.ReadAsStringAsync();
      var payloadBytes = Encoding.UTF8.GetBytes(payloadString);
      var payload = JsonConvert.DeserializeObject<WebhookPayload>(payloadString, GitHubClient.JsonSettings);

      Hook hook = null;

      if (type.Equals("org")) {
        hook = Context.Hooks.SingleOrDefault(x => x.OrganizationId == id);
      } else if (type.Equals("repo")) {
        hook = Context.Hooks.SingleOrDefault(x => x.RepositoryId == id);
      } else {
        throw new ArgumentException("Unexpected type: " + type);
      }
      
      using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(hook.Secret.ToString()))) {
        byte[] hash = hmac.ComputeHash(payloadBytes);
        // We're not worth launching a timing attack against.
        if (!hash.SequenceEqual(signature)) {
          return BadRequest("Invalid signature.");
        }
      }
      
      hook.LastSeen = DateTimeOffset.Now;
      await Context.SaveChangesAsync();

      var changeSummary = new ChangeSummary();
      
      switch (eventName) {
        case "issues":
          switch (payload.Action) {
            case "opened":
            case "closed":
            case "reopened":
            case "edited":
            case "labeled":
            case "unlabeled":
            case "assigned":
            case "unassigned":
              await HandleIssues(payload, changeSummary);
              break;
          }
          break;
        case "issue_comment":
          switch (payload.Action) {
            case "created":
            case "edited":
            case "deleted":
              await HandleIssueComment(payload, changeSummary);
              break;
          }
          break;
        case "repository":
          if (
            // Created events can only come from the org-level hook.
            payload.Action.Equals("created") ||
            // We'll get deletion events from both the repo and org, but
            // we'll ignore the org one.
            (type.Equals("repo") && payload.Action.Equals("deleted"))) {
            await HandleRepository(payload);
          }
          break;
        case "ping":
          break;
        default:
          throw new NotImplementedException($"Webhook event '{eventName}' is not handled. Either support it or don't subscribe to it.");
      }

      if (!changeSummary.Empty) {
        await _busClient.NotifyChanges(changeSummary);
      }

      return StatusCode(HttpStatusCode.Accepted);
    }

    private async Task HandleIssueComment(WebhookPayload payload, ChangeSummary changeSummary) {
      // Ensure the issue that owns this comment exists locally efore we add the comment.
      await HandleIssues(payload, changeSummary);

      using (var context = new ShipHubContext()) {
        changeSummary.UnionWith(await context.BulkUpdateAccounts(
          DateTimeOffset.Now,
          Mapper.Map<IEnumerable<AccountTableType>>(new[] { payload.Comment.User })));

        if (payload.Action.Equals("deleted")) {
          var commentsExcludingDeletion = Context.Comments
            .Where(x => x.IssueId == payload.Issue.Id && x.Id != payload.Comment.Id);
          var commentsExcludingDeletionMapped = Mapper.Map<IEnumerable<CommentTableType>>(commentsExcludingDeletion);
          changeSummary.UnionWith(await context.BulkUpdateIssueComments(
            payload.Repository.FullName,
            (int)payload.Comment.IssueNumber,
            commentsExcludingDeletionMapped,
            complete: true));
        } else {
          changeSummary.UnionWith(await context.BulkUpdateIssueComments(
            payload.Repository.FullName,
            (int)payload.Comment.IssueNumber,
            Mapper.Map<IEnumerable<CommentTableType>>(new[] { payload.Comment })));
        }
      }
    }

    private async Task HandleRepository(WebhookPayload payload) {
      if (payload.Repository.Owner.Type == GitHubAccountType.Organization) {
        var org = await Context.Organizations.SingleAsync(x => x.Id == payload.Repository.Owner.Id);
        var syncTasks = org.Members
          .Where(x => x.Token != null)
          .Select(x => _busClient.SyncAccountRepositories(x.Id, x.Login, x.Token));
        await Task.WhenAll(syncTasks);
      } else {
        // TODO: This should also trigger a sync for contributors of a repo, but at
        // least this is more correct than what we have now.
        var owner = await Context.Accounts.SingleOrDefaultAsync(x => x.Id == payload.Repository.Owner.Id);
        if (owner.Token != null) {
          await _busClient.SyncAccountRepositories(owner.Id, owner.Login, owner.Token);
        }
      }
    }

    private async Task HandleIssues(WebhookPayload payload, ChangeSummary summary) {
      if (payload.Issue.Milestone != null) {
        var milestone = Mapper.Map<MilestoneTableType>(payload.Issue.Milestone);
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

        var accountsMapped = Mapper.Map<IEnumerable<AccountTableType>>(referencedAccounts.Distinct(x => x.Id));
        summary.UnionWith(await Context.BulkUpdateAccounts(DateTimeOffset.Now, accountsMapped));
      }

      var issues = new List<Common.GitHub.Models.Issue> { payload.Issue };
      var issuesMapped = Mapper.Map<IEnumerable<IssueTableType>>(issues);

      var labels = payload.Issue.Labels?.Select(x => new LabelTableType() {
        ItemId = payload.Issue.Id,
        Color = x.Color,
        Name = x.Name
      });

      var assigneeMappings = payload.Issue.Assignees?.Select(x => new MappingTableType() {
        Item1 = payload.Issue.Id,
        Item2 = x.Id,
      });

      var issueChanges = await Context.BulkUpdateIssues(payload.Repository.Id, issuesMapped, labels, assigneeMappings);
      summary.UnionWith(issueChanges);
    }
  }
}