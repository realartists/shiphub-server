namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Data.Entity.Infrastructure;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Security.Authentication;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Filters;
  using Newtonsoft.Json;
  using Orleans;
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
    public bool NeedsReactivation { get; set; }
  }

  [RoutePrefix("billing")]
  public class BillingController : ShipHubController {
    private IShipHubConfiguration _configuration;
    private IShipHubQueueClient _queueClient;
    private IGrainFactory _grainFactory;
    private cb.ChargeBeeApi _chargeBee;

    public BillingController(IShipHubConfiguration config, IGrainFactory grainFactory, cb.ChargeBeeApi chargeBee, IShipHubQueueClient queueClient) {
      _configuration = config;
      _queueClient = queueClient;
      _grainFactory = grainFactory;
      _chargeBee = chargeBee;
    }

    private static IEnumerable<string> GetActionLines(Account account) {
      if (account.Subscription.State == SubscriptionState.Subscribed) {
        // Should server send the "Already Subscribed" place holder text?
        return new string[0];
      } else if (account is Organization) {
        return new[] {
          "$9 per active user / month",
          "$25 per month for first 5 active users",
        };
      } else {
        return new[] { "$9 per month" };
      }
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

        var orgs = await context.OrganizationAccounts
          .Where(x => x.UserId == ShipHubUser.UserId && x.Organization.Subscription != null)
          .Select(x => x.Organization)
          .Include(x => x.Subscription)
          .OrderBy(x => x.Login)
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
           CanEdit = true,
           ActionUrl = actionUrl,
           PricingLines = GetActionLines(x),
         };
       }).ToList();

      return Ok(result);
    }

    public static string CreateSignature(long actorId, long targetId) {
      return CreateSignature(actorId.ToString(), targetId.ToString());
    }

    public static string CreateSignature(string actorId, string targetId) {
      using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("N7lowJKM71PgNdwfMTDHmNb82wiwFGl"))) {
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{actorId}|{targetId}"));
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

      if (passThruContent.NeedsReactivation) {
        await _chargeBee.Subscription.Reactivate(hostedPage.Content.Subscription.Id).Request();
      }

      string accountType;
      long accountId;
      ChargeBeeUtilities.ParseCustomerId(hostedPage.Content.Subscription.CustomerId, out accountType, out accountId);

      ChangeSummary changes;
      using (var context = new ShipHubContext()) {
        var sub = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == accountId);

        if (sub == null) {
          sub = new Subscription() { AccountId = accountId, };
        }

        sub.State = SubscriptionState.Subscribed;
        sub.TrialEndDate = null;
        sub.Version = hostedPage.Content.Subscription.ResourceVersion.Value;

        changes = await context.BulkUpdateSubscriptions(new[] {
            new SubscriptionTableType(){
              AccountId = sub.AccountId,
              State = sub.StateName,
              TrialEndDate = sub.TrialEndDate,
              Version = sub.Version,
            },
          });
      }

      if (!changes.IsEmpty) {
        await _queueClient.NotifyChanges(changes);
      }

      return Redirect($"https://{_configuration.WebsiteHostName}/signup-thankyou.html");
    }

    public virtual IGitHubActor CreateGitHubActor(long userId) {
      return _grainFactory.GetGrain<IGitHubActor>(userId);
    }

    private async Task<IHttpActionResult> BuyPersonal(long actorId, long targetId) {
      var subList = (await _chargeBee.Subscription.List()
        .CustomerId().Is($"user-{targetId}")
        .PlanId().Is("personal")
        .Limit(1)
        .SortByCreatedAt(cb.Filters.Enums.SortOrderEnum.Desc)
        .Request()).List;

      if (subList.Count == 0) {
        throw new ArgumentException("Could not find existing subscription");
      }

      var sub = subList.First().Subscription;

      if (sub.Status == cbm.Subscription.StatusEnum.Active) {
        throw new ArgumentException("Existing subscription is already active");
      }
      var needsReactivation = false;
      var pageRequest = _chargeBee.HostedPage.CheckoutExisting()
        .SubscriptionId(sub.Id)
        .SubscriptionPlanId("personal")
        .Embed(false);

      bool isMemberOfPaidOrg = false;
      using (var context = new ShipHubContext()) {
        isMemberOfPaidOrg = await context.OrganizationAccounts
          .AnyAsync(x =>
            x.UserId == targetId &&
            x.Organization.Subscription.StateName == SubscriptionState.Subscribed.ToString());
      }

      string couponToAdd = null;

      if (sub.Status == cbm.Subscription.StatusEnum.InTrial) {
        if (isMemberOfPaidOrg) {
          // If you belong to a paid organization, your personal subscription
          // is complimentary.
          couponToAdd = "member_of_paid_org";
        } else {
          // Apply a coupon to make up for any unused free trial time that's
          // still remaining.  Don't want to penalize folks that decide to buy
          // before the free trial is up.
          var totalDays = (sub.TrialEnd.Value.ToUniversalTime() - DateTime.UtcNow).TotalDays;
          // Always round up to the nearest whole day.
          var daysLeftOnTrial = (int)Math.Min(30, Math.Floor(totalDays + 1));
          couponToAdd = $"trial_days_left_{daysLeftOnTrial}";
        }

        pageRequest
          // Setting trial end to 0 makes the checkout page run the charge
          // immediately rather than waiting for the trial period to end.
          .SubscriptionTrialEnd(0);
      } else if (sub.Status == cbm.Subscription.StatusEnum.Cancelled) {
        // This case would happen if the customer was a subscriber in the past, cancelled,
        // and is now returning to signup again.
        //
        // ChargeBee's CheckoutExisting flow will not re-activate a cancelled subscription
        // on its own, so we'll have to do that ourselves in the return handler.  It's a
        // bummer because it means the customer's card won't get run as part of checkout.
        // If they provide invalid CC info, they won't know it until after they've completed
        // the checkout page; the failure info will have to come in an email.
        needsReactivation = true;

        if (isMemberOfPaidOrg) {
          couponToAdd = "member_of_paid_org";
        }
      }

      // ChargeBee's hosted page will throw an error if we request to add a coupon
      // that's already been applied to this subscription.
      if (couponToAdd != null && sub.Coupons?.SingleOrDefault(x => x.CouponId() == couponToAdd) == null) {
        pageRequest.SubscriptionCoupon(couponToAdd);
      }

      pageRequest
        .RedirectUrl($"https://{_configuration.ApiHostName}/billing/buy/finish")
        .PassThruContent(JsonConvert.SerializeObject(new BuyPassThruContent() {
          NeedsReactivation = needsReactivation,
        }));

      var result = (await pageRequest.Request()).HostedPage;

      return Redirect(result.Url);
    }

    private async Task<IHttpActionResult> BuyOrganization(long actorId, long targetId, Account targetAccount) {
      var ghc = CreateGitHubActor(actorId);
      var ghcUser = (await ghc.User(GitHubCacheDetails.Empty)).Result;
      var ghcOrg = (await ghc.Organization(targetAccount.Login, GitHubCacheDetails.Empty)).Result;

      var emails = (await ghc.UserEmails(GitHubCacheDetails.Empty)).Result;
      var primaryEmail = emails.First(x => x.Primary);

      string firstName = null;
      string lastName = null;
      string companyName = ghcOrg.Name.IsNullOrWhiteSpace() ? ghcOrg.Login : ghcOrg.Name;

      // Name is optional for GitHub.
      if (!ghcUser.Name.IsNullOrWhiteSpace()) {
        var nameParts = ghcUser.Name.Trim().Split(' ');
        firstName = string.Join(" ", nameParts.Take(nameParts.Count() - 1));
        lastName = nameParts.Last();
      }

      var sub = (await _chargeBee.Subscription.List()
        .CustomerId().Is($"org-{targetId}")
        .PlanId().Is("organization")
        .Limit(1)
        .SortByCreatedAt(cb.Filters.Enums.SortOrderEnum.Desc)
        .Request()).List.FirstOrDefault()?.Subscription;

      if (sub != null) {
        // Customers with past subscriptions have to use the checkout existing flow.
        var updateRequest = _chargeBee.Customer.Update($"org-{targetId}")
          .AddParam("cf_github_username", targetAccount.Login)
          .Company(companyName);

        if (firstName != null) {
          updateRequest.FirstName(firstName);
        }

        if (lastName != null) {
          updateRequest.LastName(lastName);
        }

        await updateRequest.Request();

        var result = (await _chargeBee.HostedPage.CheckoutExisting()
          .SubscriptionId(sub.Id)
          .SubscriptionPlanId("organization")
          .Embed(false)
          // ChargeBee's CheckoutExisting flow will not re-activate a cancelled subscription
          // on its own, so we'll have to do that ourselves in the return handler.  It's a
          // bummer because it means the customer's card won't get run as part of checkout.
          // If they provide invalid CC info, they won't know it until after they've completed
          // the checkout page; the failure info will have to come in an email.
          .PassThruContent(JsonConvert.SerializeObject(new BuyPassThruContent() {
            NeedsReactivation = true,
          }))
          .RedirectUrl($"https://{_configuration.ApiHostName}/billing/buy/finish")
          .Request()).HostedPage;

        return Redirect(result.Url);
      } else {
        var checkoutRequest = _chargeBee.HostedPage.CheckoutNew()
       .CustomerId($"org-{targetId}")
       .CustomerEmail(primaryEmail.Email)
       .CustomerCompany(companyName)
       .SubscriptionPlanId("organization")
       .AddParam("customer[cf_github_username]", ghcOrg.Login)
       .Embed(false)
       .PassThruContent(JsonConvert.SerializeObject(new BuyPassThruContent() {
         NeedsReactivation = false,
       }))
       .RedirectUrl($"https://{_configuration.ApiHostName}/billing/buy/finish");

        if (!firstName.IsNullOrWhiteSpace()) {
          checkoutRequest.CustomerFirstName(firstName);
        }

        if (!lastName.IsNullOrWhiteSpace()) {
          checkoutRequest.CustomerLastName(lastName);
        }

        var checkoutResult = (await checkoutRequest.Request()).HostedPage;

        return Redirect(checkoutResult.Url);
      }
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("buy/{actorId}/{targetId}/{signature}")]
    public async Task<IHttpActionResult> Buy(long actorId, long targetId, string signature) {
      if (!CreateSignature(actorId, targetId).Equals(signature)) {
        return BadRequest("Signature does not match.");
      }

      Account targetAccount;
      using (var context = new ShipHubContext()) {
        targetAccount = await context.Accounts.SingleAsync(x => x.Id == targetId);
      }

      if (targetAccount is Organization) {
        return await BuyOrganization(actorId, targetId, targetAccount);
      } else {
        return await BuyPersonal(actorId, targetId);
      }
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

    private static readonly WinHttpHandler _HttpHandler = new WinHttpHandler() {
      AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
      AutomaticRedirection = true,
      CheckCertificateRevocationList = true,
      CookieUsePolicy = CookieUsePolicy.IgnoreCookies,
      MaxAutomaticRedirections = 3,
      SslProtocols = SslProtocols.Tls12,
      WindowsProxyUsePolicy = WindowsProxyUsePolicy.DoNotUseProxy,
      // The default timeout values are all longer than our overall timeout.
    };

    private static readonly HttpClient _HttpClient = new HttpClient(_HttpHandler) {
      Timeout = _HandlerTimeout
    };

    private async Task<HttpResponseMessage> DownloadEntity(cba.EntityRequest<Type> entityResult, string entityId, string fileName, string signature, CancellationToken cancellationToken) {
      if (!CreateSignature(entityId, entityId).Equals(signature)) {
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
