namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Linq;
  using System.Net;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http;
  using AutoMapper;
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
        default:
          // TODO: Log these, since we won't have access to the hook debugger
          return StatusCode(HttpStatusCode.InternalServerError);
      }

      return StatusCode(HttpStatusCode.Accepted);
    }

    private async Task HandleIssueUpdate(JObject data) {
      var serializer = JsonSerializer.CreateDefault(GitHubClient.JsonSettings);

      var item = data["issue"].ToObject<Common.GitHub.Models.Issue>(serializer);
      long repositoryId = data["repository"]["id"].Value<long>();

      var issues = new List<Common.GitHub.Models.Issue> { item };

      var config = new MapperConfiguration(cfg => {
        cfg.AddProfile<GitHubToDataModelProfile>();
      });
      var mapper = config.CreateMapper();
      var issuesMapped = mapper.Map<IEnumerable<IssueTableType>>(issues);
      
      var labels = item.Labels.Select(x => new LabelTableType() {
        ItemId = item.Id,
        Color = x.Color,
        Name = x.Name
      });

      ChangeSummary changeSummary = await Context.BulkUpdateIssues(repositoryId, issuesMapped, labels);

      await _busClient.NotifyChanges(changeSummary);
    }
  }
}