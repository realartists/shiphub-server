namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.IO;
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
  public class GitHubWebhookController : ApiController {
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
    [Route("webhook")]
    public async Task<IHttpActionResult> HandleHook() {
      if (Request.Headers.UserAgent.Single().Product.Name != GitHubUserAgent) {
        return BadRequest("Not you.");
      }

      var eventName = Request.Headers.GetValues(EventHeaderName).Single();
      var deliveryIdHeader = Request.Headers.GetValues(DeliveryIdHeaderName).Single();
      var signatureHeader = Request.Headers.GetValues(SignatureHeaderName).Single();

      // header of form "sha1=..."
      var signature = SoapHexBinary.Parse(signatureHeader.Substring(5));
      var deliveryId = Guid.Parse(deliveryIdHeader);

      // lookup hook
      // TODO: Look this up from the hook id
      var secret = "698DACE9-6267-4391-9B1C-C6F74DB43710";
      var secretBytes = Encoding.ASCII.GetBytes(secret);

      JObject data;

      using (var hmac = new HMACSHA1(secretBytes))
      using (var bodyStream = await Request.Content.ReadAsStreamAsync())
      using (var hmacStream = new CryptoStream(bodyStream, hmac, CryptoStreamMode.Read))
      using (var textReader = new StreamReader(hmacStream, Encoding.UTF8)) {
        var json = await textReader.ReadToEndAsync();
        textReader.Close();
        // We're not worth launching a timing attack against.
        if (signature.Value.SequenceEqual(hmac.Hash)) {
          data = JsonConvert.DeserializeObject<JObject>(json, GitHubClient.JsonSettings);
        } else {
          return BadRequest("Invalid signature.");
        }
      }

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
        Id = item.Id,
        Color = x.Color,
        Name = x.Name
      });

      var context = new ShipHubContext();
      ChangeSummary changeSummary = await context.BulkUpdateIssues(repositoryId, issuesMapped, labels);

      await _busClient.NotifyChanges(changeSummary);
    }
  }
}