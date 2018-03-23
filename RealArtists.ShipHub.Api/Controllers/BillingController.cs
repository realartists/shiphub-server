namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Mixpanel;
  using Newtonsoft.Json;
  using QueueClient;
  using cb = ChargeBee;
  using cba = ChargeBee.Api;
  using cbm = ChargeBee.Models;

  public class BillingAccount {
    public long Identifier { get; set; }
    public string Login { get; set; }
    public string AvatarUrl { get; set; }
    [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
    public string Type { get; set; }
  }

  public class BillingAccountRow {
    public BillingAccount Account { get; set; }
    public bool Subscribed { get; set; }
    public bool CanEdit { get; set; }
    public string ActionUrl { get; set; }
    public IEnumerable<string> PricingLines { get; set; }
  }

  public class BuyPassThruContent {
    public long ActorId { get; set; }
    public string ActorLogin { get; set; }
    public string AnalyticsId { get; set; }
  }

  public class ThankYouPageHashParameters {
    public int Value { get; set; }
    public string PlanId { get; set; }
  }

  [RoutePrefix("billing")]
  public class BillingController : ShipHubApiController {
    private const string EndOfLifeBlogPostUrl = "https://www.realartists.com/blog/move-to-trash.html";

    private IShipHubConfiguration _configuration;
    private IShipHubQueueClient _queueClient;
    private cb.ChargeBeeApi _chargeBee;
    private IMixpanelClient _mixpanelClient;

    public BillingController(IShipHubConfiguration config, cb.ChargeBeeApi chargeBee, IShipHubQueueClient queueClient, IMixpanelClient mixpanelClient) {
      _configuration = config;
      _queueClient = queueClient;
      _chargeBee = chargeBee;
      _mixpanelClient = mixpanelClient;
    }

    [HttpGet]
    [Route("accounts")]
    public async Task<IHttpActionResult> Accounts() {
      var combined = new List<Account>();

      using (var context = new ShipHubContext()) {
        var user = await context.Users
          .Include(x => x.Subscription)
          .Where(x => x.Subscription != null)
          .SingleOrDefaultAsync(x => x.Id == ShipHubUser.UserId);

        if (user != null) {
          combined.Add(user);
        }

        var orgs = await context.AccountRepositories
          .Where(x => (
            x.AccountId == ShipHubUser.UserId &&
            x.Repository.Account is Organization &&
            x.Repository.Account.Subscription != null))
          .Select(x => x.Repository.Account)
          .GroupBy(x => x.Id)
          .Select(x => x.FirstOrDefault())
          .Include(x => x.Subscription)
          .ToArrayAsync();

        combined.AddRange(orgs);
      }

      var result = combined
       .Select(x => {
         var hasSubscription = x.Subscription.State == SubscriptionState.Subscribed;
         var signature = CreateSignature(ShipHubUser.UserId, x.Id);
         var apiHostName = _configuration.ApiHostName;
         var actionUrl = $"https://{apiHostName}/billing/{(hasSubscription ? "manage" : "buy")}/{ShipHubUser.UserId}/{x.Id}/{signature}";

         return new BillingAccountRow() {
           Account = new BillingAccount() {
             Identifier = x.Id,
             Login = x.Login,
             // TODO: Sync avatars and return real values here.
             AvatarUrl = "https://avatars.githubusercontent.com/u/335107?v=3",
             Type = (x is User) ? "user" : "organization",
           },
           Subscribed = hasSubscription,
           // TODO: Only allow edits for purchaser or admins.
           CanEdit = hasSubscription,
           ActionUrl = actionUrl,
           PricingLines = new[] { "Free" },
         };
       }).ToList();

      return Ok(result);
    }

    public static string CreateSignature(long actorId, long targetId) {
      return CreateSignature($"{actorId}|{targetId}");
    }

    public static string CreateLegacySignature(string input) {
      return CreateSignature($"{input}|{input}");
    }

    public static string CreateSignature(string value) {
      using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("N7lowJKM71PgNdwfMTDHmNb82wiwFGl"))) {
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        var hashString = string.Join("", hash.Select(x => x.ToString("x2")));
        return hashString.Substring(0, 8);
      }
    }

    [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "state")]
    [AllowAnonymous]
    [HttpGet]
    [Route("buy/finish")]
    public async Task<IHttpActionResult> BuyFinish(string id, string state) {
      var hostedPage = (await _chargeBee.HostedPage.Retrieve(id).Request()).HostedPage;

      if (hostedPage.State != cbm.HostedPage.StateEnum.Succeeded) {
        // We should only get here if the signup was completed.
        throw new InvalidOperationException("Asked to complete signup for subscription when checkout did not complete.");
      }

      var passThruContent = JsonConvert.DeserializeObject<BuyPassThruContent>(hostedPage.PassThruContent);

      ChargeBeeUtilities.ParseCustomerId(hostedPage.Content.Subscription.CustomerId, out var accountType, out var accountId);

      ChangeSummary changes;
      using (var context = new ShipHubContext()) {
        changes = await context.BulkUpdateSubscriptions(new[] {
            new SubscriptionTableType(){
              AccountId = accountId,
              State = SubscriptionState.Subscribed.ToString(),
              TrialEndDate = null,
              Version = hostedPage.Content.Subscription.ResourceVersion.Value,
            },
          });
      }

      if (!changes.IsEmpty) {
        await _queueClient.NotifyChanges(changes);
      }

      if (passThruContent.AnalyticsId != null) {
        await _mixpanelClient.TrackAsync(
          "Purchased",
          passThruContent.AnalyticsId,
          new {
            plan = hostedPage.Content.Subscription.PlanId,
            customer_id = hostedPage.Content.Subscription.CustomerId,
            // These refer to the account performing the action, which in the case of
            // an org subscription, is different than the account being purchased.
            _github_id = passThruContent.ActorId,
            _github_login = passThruContent.ActorLogin,
          });
      }

      var hashParams = new ThankYouPageHashParameters() {
        Value = hostedPage.Content.Subscription.PlanUnitPrice.Value / 100,
        PlanId = hostedPage.Content.Subscription.PlanId,
      };
      var hashParamBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(
        JsonConvert.SerializeObject(hashParams, GitHubSerialization.JsonSerializerSettings)));

      return Redirect($"https://{_configuration.WebsiteHostName}/signup-thankyou.html#{WebUtility.UrlEncode(hashParamBase64)}");
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("buy/{actorId}/{targetId}/{signature}")]
    public IHttpActionResult Buy(long actorId, long targetId, string signature) {
      if (!CreateSignature(actorId, targetId).Equals(signature)) {
        return BadRequest("Signature does not match.");
      }

      return Redirect(EndOfLifeBlogPostUrl);
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("manage/{actorId}/{targetId}/{signature}")]
    public async Task<IHttpActionResult> Manage(long actorId, long targetId, string signature) {
      if (!CreateSignature(actorId, targetId).Equals(signature)) {
        return BadRequest("Signature does not match.");
      }

      Account account;
      using (var context = new ShipHubContext()) {
        account = await context.Accounts.SingleAsync(x => x.Id == targetId);
      }
      var customerIdPrefix = (account is Organization) ? "org" : "user";

      var result = (await _chargeBee.PortalSession.Create()
        // The redirect URL tells ChargeBee where to send the user if they click
        // the logout link.  Our ChargeBee theme hides this logout link, so this
        // isn't used but is still a required param.
        .RedirectUrl($"https://{_configuration.WebsiteHostName}")
        .CustomerId($"{customerIdPrefix}-{targetId}")
        .Request()).PortalSession;

      return Redirect(result.AccessUrl);
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("update/{accountId}/{signature}")]
    public async Task<IHttpActionResult> UpdatePaymentMethod(long accountId, string signature) {
      if (!CreateSignature(accountId, accountId).Equals(signature)) {
        return BadRequest("Signature does not match.");
      }

      Account account;
      using (var context = new ShipHubContext()) {
        account = await context.Accounts.SingleAsync(x => x.Id == accountId);
      }
      var customerIdPrefix = (account is Organization) ? "org" : "user";

      var result = await _chargeBee.HostedPage.UpdatePaymentMethod()
        .CustomerId($"{customerIdPrefix}-{accountId}")
        .Embed(false)
        .Request();

      return Redirect(result.HostedPage.Url);
    }

    private static readonly TimeSpan _HandlerTimeout = TimeSpan.FromSeconds(60);
    private static readonly HttpClient _HttpClient = new HttpClient(HttpUtilities.CreateDefaultHandler(maxRedirects: 3)) {
      Timeout = _HandlerTimeout
    };

    private async Task<HttpResponseMessage> DownloadEntity(cba.EntityRequest<Type> entityResult, string entityId, string fileName, string signature, CancellationToken cancellationToken) {
      // Allow old and new style signatures.
      if (!CreateSignature(entityId).Equals(signature)
        && !CreateLegacySignature(entityId).Equals(signature)) {
        return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "Signature does not match.");
      }

      var downloadUrl = (await entityResult.Request()).Download.DownloadUrl;
      var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);

      using (var timeout = new CancellationTokenSource(_HandlerTimeout))
      using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token)) {
        try {
          var response = await _HttpClient.SendAsync(request, linkedCancellation.Token);
          response.Headers.Remove("Server");
          response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") {
            FileName = fileName
          };
          response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
          return response;
        } catch (TaskCanceledException exception) {
          return Request.CreateErrorResponse(HttpStatusCode.GatewayTimeout, exception);
        }
      }
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("invoice/{invoiceId}/{signature}/{fileName}")]
    public Task<HttpResponseMessage> DownloadInvoice(string invoiceId, string fileName, string signature, CancellationToken cancellationToken) {
      return DownloadEntity(_chargeBee.Invoice.Pdf(invoiceId), invoiceId, fileName, signature, cancellationToken);
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("credit/{creditNoteId}/{signature}/{fileName}")]
    public Task<HttpResponseMessage> DownloadCreditNote(string creditNoteId, string fileName, string signature, CancellationToken cancellationToken) {
      return DownloadEntity(_chargeBee.CreditNote.Pdf(creditNoteId), creditNoteId, fileName, signature, cancellationToken);
    }
  }
}
