namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics.CodeAnalysis;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces.GitHub;
  using ChargeBee.Api;
  using ChargeBee.Models;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Filters;
  using Orleans;

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

  [RoutePrefix("billing")]
  public class BillingController : ShipHubController {
    private IShipHubConfiguration _configuration;
    private IGrainFactory _grainFactory;

    public BillingController(IShipHubConfiguration config, IGrainFactory grainFactory) {
      _configuration = config;
      _grainFactory = grainFactory;
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
      var principal = RequestContext.Principal as ShipHubPrincipal;

      var combined = new List<Account>();

      var user = await Context.Users
        .Include(x => x.Subscription)
        .SingleAsync(x => x.Id == principal.UserId);
      if (user.Subscription != null) {
        combined.Add(user);
      }

      var orgs = await Context.OrganizationAccounts
        .Include(x => x.Organization.Subscription)
        .Where(x => x.UserId == principal.UserId && x.Organization.Subscription != null)
        .Select(x => x.Organization)
        .OrderBy(x => x.Login)
        .ToArrayAsync();
      combined.AddRange(orgs);

      var result = combined
       .Select(x => {
         var hasSubscription = x.Subscription.State == SubscriptionState.Subscribed;
         var signature = CreateSignature(principal.UserId, x.Id);
         var apiHostName = _configuration.ApiHostName;
         var actionUrl = $"https://{apiHostName}/billing/{(hasSubscription ? "manage" : "buy")}/{principal.UserId}/{x.Id}/{signature}";

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
       })
        .ToList();

      if (result.Count > 0) {
        return Ok(result);
      } else {
        // We found no Subscription records in the db for these accounts, which can only mean
        // that we're not correctly sync'ing with ChargeBee.  In that case, let's just show an
        // error.
        return Content(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string>() {
          { "message", "Subscription info for your accounts have not loaded yet. Try again later." },
        });
      }
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
    [Route("reactivate")]
    public IHttpActionResult Reactivate(string id, string state) {
      var hostedPage = HostedPage.Retrieve(id).Request().HostedPage;

      if (hostedPage.State != HostedPage.StateEnum.Succeeded) {
        // ChargeBee should never send the user here unless checkout was successufl.
        throw new ArgumentException("Asked to reactivate a subscription when checkout did not complete.");
      }

      ChargeBee.Models.Subscription.Reactivate(hostedPage.Content.Subscription.Id).Request();
      var host = new Uri(hostedPage.Url).Host;
      return Redirect($"https://{host}/pages/v2/{id}/thank_you");
    }

    public virtual IGitHubActor CreateGitHubActor(User user) {
      return _grainFactory.GetGrain<IGitHubActor>(user.Id);
    }

    private async Task<IHttpActionResult> BuyPersonal(long actorId, long targetId) {
      var subList = ChargeBee.Models.Subscription.List()
        .CustomerId().Is($"user-{targetId}")
        .PlanId().Is("personal")
        .Limit(1)
        .SortByCreatedAt(ChargeBee.Filters.Enums.SortOrderEnum.Desc)
        .Request().List;

      if (subList.Count == 0) {
        throw new ArgumentException("Could not find existing subscription");
      }

      var sub = subList.First().Subscription;

      if (sub.Status == ChargeBee.Models.Subscription.StatusEnum.Active) {
        throw new ArgumentException("Existing subscription is already active");
      }

      var pageRequest = HostedPage.CheckoutExisting()
        .SubscriptionId(sub.Id)
        .SubscriptionPlanId("personal")
        .Embed(false);

      var isMemberOfPaidOrg = await Context.OrganizationAccounts
        .CountAsync(x =>
          x.UserId == targetId &&
          x.Organization.Subscription.StateName == SubscriptionState.Subscribed.ToString()) > 0;

      string couponToAdd = null;

      if (sub.Status == ChargeBee.Models.Subscription.StatusEnum.InTrial) {
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
      } else if (sub.Status == ChargeBee.Models.Subscription.StatusEnum.Cancelled) {
        // This case would happen if the customer was a subscriber in the past, cancelled,
        // and is now returning to signup again.
        //
        // ChargeBee's CheckoutExisting flow will not re-activate a cancelled subscription
        // on its own, so we'll have to do that ourselves in the return handler.  It's a
        // bummer because it means the customer's card won't get run as part of checkout.
        // If they provide invalid CC info, they won't know it until after they've completed
        // the checkout page; the failure info will have to come in an email.
        var apiHostName = _configuration.ApiHostName;
        pageRequest.RedirectUrl($"https://{apiHostName}/billing/reactivate");

        if (isMemberOfPaidOrg) {
          couponToAdd = "member_of_paid_org";
        }
      }

      // ChargeBee's hosted page will throw an error if we request to add a coupon
      // that's already been applied to this subscription.
      if (couponToAdd != null && sub.Coupons?.SingleOrDefault(x => x.CouponId() == couponToAdd) == null) {
        pageRequest.SubscriptionCoupon(couponToAdd);
      }

      var result = pageRequest.Request().HostedPage;

      return Redirect(result.Url);
    }

    private async Task<IHttpActionResult> BuyOrganization(long actorId, long targetId, Account targetAccount) {
      var user = await Context.Users.SingleAsync(x => x.Id == actorId);

      var ghc = CreateGitHubActor(user);
      var ghcUser = (await ghc.User(GitHubCacheDetails.Empty)).Result;
      var ghcOrg = (await ghc.Organization(targetAccount.Login, GitHubCacheDetails.Empty)).Result;

      var emails = (await ghc.UserEmails(GitHubCacheDetails.Empty)).Result;
      var primaryEmail = emails.First(x => x.Primary);

      string firstName = null;
      string lastName = null;
      string companyName = ghcOrg.Name.IsNullOrWhiteSpace() ? null : ghcOrg.Name;

      // Name is optional for Github.
      if (!ghcUser.Name.IsNullOrWhiteSpace()) {
        var nameParts = ghcUser.Name.Trim().Split(' ');
        firstName = string.Join(" ", nameParts.Take(nameParts.Count() - 1));
        lastName = nameParts.Last();
      }

      var sub = ChargeBee.Models.Subscription.List()
        .CustomerId().Is($"org-{targetId}")
        .PlanId().Is("organization")
        .Limit(1)
        .SortByCreatedAt(ChargeBee.Filters.Enums.SortOrderEnum.Desc)
        .Request().List.FirstOrDefault()?.Subscription;

      if (sub != null) {
        // Customers with past subscriptions have to use the checkout existing flow.
        var updateRequest = Customer.Update($"org-{targetId}")
          .Param("cf_github_username", targetAccount.Login);

        if (firstName != null) {
          updateRequest.FirstName(firstName);
        }

        if (lastName != null) {
          updateRequest.LastName(lastName);
        }

        if (companyName != null) {
          updateRequest.Company(companyName);
        }

        updateRequest.Request();

        var result = HostedPage.CheckoutExisting()
          .SubscriptionId(sub.Id)
          .SubscriptionPlanId("organization")
          .Embed(false)
          // ChargeBee's CheckoutExisting flow will not re-activate a cancelled subscription
          // on its own, so we'll have to do that ourselves in the return handler.  It's a
          // bummer because it means the customer's card won't get run as part of checkout.
          // If they provide invalid CC info, they won't know it until after they've completed
          // the checkout page; the failure info will have to come in an email.
          .RedirectUrl($"https://{_configuration.ApiHostName}/billing/reactivate")
          .Request().HostedPage;

        return Redirect(result.Url);
      } else {
        var checkoutRequest = HostedPage.CheckoutNew()
       .CustomerId($"org-{targetId}")
       .CustomerEmail(primaryEmail.Email)
       .SubscriptionPlanId("organization")
       .Param("customer[cf_github_username]", ghcOrg.Login)
       .Embed(false);

        if (!ghcOrg.Name.IsNullOrWhiteSpace()) {
          checkoutRequest.CustomerCompany(ghcOrg.Name);
        }

        if (!firstName.IsNullOrWhiteSpace()) {
          checkoutRequest.CustomerFirstName(firstName);
        }

        if (!lastName.IsNullOrWhiteSpace()) {
          checkoutRequest.CustomerLastName(lastName);
        }

        var checkoutResult = checkoutRequest.Request().HostedPage;

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

      var targetAccount = await Context.Accounts.SingleAsync(x => x.Id == targetId);

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

      var account = await Context.Accounts.SingleAsync(x => x.Id == targetId);
      var customerIdPrefix = (account is Organization) ? "org" : "user";

      var result = PortalSession.Create()
        .RedirectUrl("https://www.realartists.com")
        .CustomerId($"{customerIdPrefix}-{targetId}")
        .Request().PortalSession;

      return Redirect(result.AccessUrl);
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("update/{accountId}/{signature}")]
    public async Task<IHttpActionResult> UpdatePaymentMethod(long accountId, string signature) {

      if (!CreateSignature(accountId, accountId).Equals(signature)) {
        return BadRequest("Signature does not match.");
      }

      var account = await Context.Accounts.SingleAsync(x => x.Id == accountId);
      var customerIdPrefix = (account is Organization) ? "org" : "user";

      var result = HostedPage.UpdatePaymentMethod()
        .CustomerId($"{customerIdPrefix}-{accountId}")
        .Embed(false)
        .Request();

      return Redirect(result.HostedPage.Url);
    }

    private static HttpClient _HttpClient { get; } = new HttpClient();

    private async Task<HttpResponseMessage> DownloadEntity(EntityRequest<Type> entityResult, string entityId, string fileName, string signature, CancellationToken cancellationToken) {
      if (!CreateSignature(entityId, entityId).Equals(signature)) {
        return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "Signature does not match.");
      }

      var downloadUrl = entityResult.Request().Download.DownloadUrl;
      var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, downloadUrl);

      try {
        var response = await _HttpClient.SendAsync(request, cancellationToken);
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") {
          FileName = fileName
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return response;
      } catch (TaskCanceledException exception) {
        return Request.CreateErrorResponse(HttpStatusCode.GatewayTimeout, exception);
      }
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("invoice/{invoiceId}/{signature}/{fileName}")]
    public Task<HttpResponseMessage> DownloadInvoice(string invoiceId, string fileName, string signature, CancellationToken cancellationToken) {
      return DownloadEntity(Invoice.Pdf(invoiceId), invoiceId, fileName, signature, cancellationToken);
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("credit/{creditNoteId}/{signature}/{fileName}")]
    public Task<HttpResponseMessage> DownloadCreditNote(string creditNoteId, string fileName, string signature, CancellationToken cancellationToken) {
      return DownloadEntity(CreditNote.Pdf(creditNoteId), creditNoteId, fileName, signature, cancellationToken);
    }
  }
}
