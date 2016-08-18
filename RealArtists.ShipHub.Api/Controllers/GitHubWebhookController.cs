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

      var payload = await Request.Content.ReadAsStringAsync();
      var payloadBytes = Encoding.UTF8.GetBytes(payload);
      var data = JsonConvert.DeserializeObject<JObject>(payload, GitHubClient.JsonSettings);

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
      
      switch (eventName) {
        case "issues":
          var actions = new string[] {
            "opened",
            "closed",
            "reopened",
            "edited",
            "labeled",
            "unlabeled",
            "assigned",
            "unassigned",
          };

          if (actions.Contains(data["action"].ToString())) {
            await HandleIssueUpdate(data);
          }
          break;
        case "ping":
          break;
        case "repository":
          string action = data["action"].ToString();
          if (
            // Created events can only come from the org-level hook.
            action.Equals("created") ||
            // We'll get deletion events from both the repo and org, but
            // we'll ignore the org one.
            type.Equals("repo") && (action.Equals("deleted"))) {
            await HandleRepositoryCreatedOrDeleted(data);
          }
          break;
        default:
          throw new NotImplementedException($"Webhook event '{eventName}' is not handled. Either support it or don't subscribe to it.");
      }

      return StatusCode(HttpStatusCode.Accepted);
    }

    private async Task HandleRepositoryCreatedOrDeleted(JObject data) {
      var serializer = JsonSerializer.CreateDefault(GitHubClient.JsonSettings);
      var repository = data["repository"].ToObject<Common.GitHub.Models.Repository>(serializer);

      if (repository.Owner.Type != Common.GitHub.Models.GitHubAccountType.Organization) {
        throw new InvalidOperationException("Should only receive repo created events for repo's owned by organizations.");
      }
      
      var org = await Context.Organizations.SingleAsync(x => x.Id == repository.Owner.Id);
      var syncTasks = org.Members
        .Where(x => x.Token != null)
        .Select(x => _busClient.SyncAccountRepositories(x.Id, x.Login, x.Token));

      await Task.WhenAll(syncTasks);
    }

    private async Task HandleIssueUpdate(JObject data) {
      var serializer = JsonSerializer.CreateDefault(GitHubClient.JsonSettings);

      var issue = data["issue"].ToObject<Common.GitHub.Models.Issue>(serializer);
      long repositoryId = data["repository"]["id"].Value<long>();

      var summary = new ChangeSummary();

      if (issue.Milestone != null) {
        var milestone = Mapper.Map<MilestoneTableType>(issue.Milestone);
        var milestoneSummary = await Context.BulkUpdateMilestones(
          repositoryId,
          new MilestoneTableType[] { milestone });
        summary.UnionWith(milestoneSummary);
      }

      var referencedAccounts = new List<Common.GitHub.Models.Account>();
      referencedAccounts.Add(issue.User);
      if (issue.Assignees != null) {
        referencedAccounts.AddRange(issue.Assignees);
      }
      if (issue.ClosedBy != null) {
        referencedAccounts.Add(issue.ClosedBy);
      }

      if (referencedAccounts.Count > 0) {
        var accountsMapped = Mapper.Map<IEnumerable<AccountTableType>>(referencedAccounts)
          // Dedup the list
          .GroupBy(x => x.Id)
          .Select(x => x.First());
        summary.UnionWith(await Context.BulkUpdateAccounts(DateTimeOffset.Now, accountsMapped));
      }

      var issues = new List<Common.GitHub.Models.Issue> { issue };
      var issuesMapped = Mapper.Map<IEnumerable<IssueTableType>>(issues);

      var labels = issue.Labels?.Select(x => new LabelTableType() {
        ItemId = issue.Id,
        Color = x.Color,
        Name = x.Name
      });

      var assigneeMappings = issue.Assignees?.Select(x => new MappingTableType() {
        Item1 = issue.Id,
        Item2 = x.Id,
      });

      var issueChanges = await Context.BulkUpdateIssues(repositoryId, issuesMapped, labels, assigneeMappings);
      summary.UnionWith(issueChanges);

      if (!summary.Empty) {
        await _busClient.NotifyChanges(summary);
      }
    }
  }
}