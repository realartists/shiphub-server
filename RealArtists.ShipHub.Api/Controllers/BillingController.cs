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
  using System.Web.Http.Results;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Mixpanel;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
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
    public long ActorId { get; set; }
    public string ActorLogin { get; set; }
    public string AnalyticsId { get; set; }
    public bool NeedsReactivation { get; set; }
  }

  public class ThankYouPageHashParams {
    public int Value { get; set; }
    public string PlanId { get; set; }
  }

  [RoutePrefix("billing")]
  public class BillingController : ShipHubController {
    private IShipHubConfiguration _configuration;
    private IShipHubQueueClient _queueClient;
    private IGrainFactory _grainFactory;
    private cb.ChargeBeeApi _chargeBee;
    private IMixpanelClient _mixpanelClient;

    public BillingController(IShipHubConfiguration config, IGrainFactory grainFactory, cb.ChargeBeeApi chargeBee, IShipHubQueueClient queueClient, IMixpanelClient mixpanelClient) {
      _configuration = config;
      _queueClient = queueClient;
      _grainFactory = grainFactory;
      _chargeBee = chargeBee;
      _mixpanelClient = mixpanelClient;
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
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{actorId}|{targetId}"));
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

      var hashParams = new ThankYouPageHashParams() {
        Value = hostedPage.Content.Subscription.PlanUnitPrice.Value / 100,
        PlanId = hostedPage.Content.Subscription.PlanId,
      };
      var hashParamBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(
        JsonConvert.SerializeObject(hashParams, GitHubSerialization.JsonSerializerSettings)));

      return Redirect($"https://{_configuration.WebsiteHostName}/signup-thankyou.html#{WebUtility.UrlEncode(hashParamBase64)}");
    }

    public virtual IGitHubActor CreateGitHubActor(long userId) {
      return _grainFactory.GetGrain<IGitHubActor>(userId);
    }

    private async Task<RedirectResult> BuyPersonal(Account actorAccount, long targetId, string analyticsId = null) {
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

      var subMetaData = (sub.MetaData ?? new JObject()).ToObject<ChargeBeePersonalSubscriptionMetadata>(GitHubSerialization.JsonSerializer);

      var needsReactivation = false;
      var pageRequest = _chargeBee.HostedPage.CheckoutExisting()
        .SubscriptionId(sub.Id)
        .SubscriptionPlanId("personal")
        .Embed(false);

      var isMemberOfPaidOrg = false;
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
          var trialPeriodDays = subMetaData.TrialPeriodDays ?? 30;

          // Always round up to the nearest whole day.
          var daysLeftOnTrial = (int)Math.Min(trialPeriodDays, Math.Floor(totalDays + 1));

          if (trialPeriodDays == 30) {
            // The original coupons for 30 day trials didn't include total days in the name.
            couponToAdd = $"trial_days_left_{daysLeftOnTrial}";
          } else {
            couponToAdd = $"trial_days_left_{daysLeftOnTrial}_of_{trialPeriodDays}";
          }
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
          ActorId = actorAccount.Id,
          ActorLogin = actorAccount.Login,
          AnalyticsId = analyticsId,
          NeedsReactivation = needsReactivation,
        }));

      var result = (await pageRequest.Request()).HostedPage;

      return Redirect(result.Url);
    }

    private async Task<RedirectResult> BuyOrganization(Account actorAccount, long targetId, Account targetAccount, string analyticsId = null) {
      var ghc = CreateGitHubActor(actorAccount.Id);
      var ghcUser = (await ghc.User()).Result;
      var ghcOrg = (await ghc.Organization(targetAccount.Login)).Result;

      var emails = (await ghc.UserEmails()).Result;
      var primaryEmail = emails.First(x => x.Primary);

      string firstName = null;
      string lastName = null;
      var companyName = ghcOrg.Name.IsNullOrWhiteSpace() ? ghcOrg.Login : ghcOrg.Name;

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
            ActorId = actorAccount.Id,
            ActorLogin = actorAccount.Login,
            AnalyticsId = analyticsId,
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
         ActorId = actorAccount.Id,
         ActorLogin = actorAccount.Login,
         AnalyticsId = analyticsId,
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
    public async Task<IHttpActionResult> Buy(long actorId, long targetId, string signature, [FromUri(Name = "analytics_id")] string analyticsId = null) {
      if (!CreateSignature(actorId, targetId).Equals(signature)) {
        return BadRequest("Signature does not match.");
      }

      Account actorAccount;
      Account targetAccount;
      using (var context = new ShipHubContext()) {
        actorAccount = await context.Accounts.SingleAsync(x => x.Id == actorId);
        targetAccount = await context.Accounts.SingleAsync(x => x.Id == targetId);
      }

      RedirectResult result;
      if (targetAccount is Organization) {
        result = await BuyOrganization(actorAccount, targetId, targetAccount, analyticsId);
      } else {
        result = await BuyPersonal(actorAccount, targetId, analyticsId);
      }

      if (analyticsId != null) {
        await _mixpanelClient.TrackAsync(
          "Redirect to ChargeBee",
          analyticsId,
          new {
            _github_id = actorAccount.Id,
            _github_login = actorAccount.Login,
          });
      }

      return result;
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

    private static readonly HttpMessageHandler _HttpHandler = HttpUtilities.CreateDefaultHandler(maxRedirects: 3);

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
