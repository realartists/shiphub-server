﻿namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ChargeBee.Models;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Filters;
  using Microsoft.Azure;

  public class BillingAccount {
    public long Identifier { get; set; }
    public string Login { get; set; }
    public string AvatarUrl { get; set; }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
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
    private string ApiHostname {
      get {
        var apiHostname = CloudConfigurationManager.GetSetting("ApiHostname");
        if (apiHostname == null) {
          throw new ApplicationException("ApiHostname not specified in configuration.");
        }
        return apiHostname;
      }
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
          var actionUrl = $"https://{ApiHostname}/billing/{(hasSubscription ? "manage" : "buy")}/{principal.UserId}/{x.Id}/{signature}";

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
      using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes("N7lowJKM71PgNdwfMTDHmNb82wiwFGl"))) {
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{actorId}|{targetId}"));
        var hashString = string.Join("", hash.Select(x => x.ToString("x2")));
        return hashString;
      }
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("reactivate")]
    public async Task<IHttpActionResult> Reactivate(string id, string state) {
      var hostedPage = HostedPage.Retrieve(id).Request().HostedPage;

      if (hostedPage.State != HostedPage.StateEnum.Succeeded) {
        // ChargeBee should never send the user here unless checkout was successufl.
        throw new ArgumentException("Asked to reactivate a subscription when checkout did not complete.");
      }

      // ChargeBee bug workaround - the "resource_version" value we get in the
      // "subscription_reactivated" event is bogus.  ChargeBee's working on a fix.
      // The resource version is supposed to give us a timestamp of millis since
      // 1970, and we use this in the webhook handler to ignore events with out-dated
      // info.
      //
      // We can use another timestamp field in the webhook event to workaround, but
      // unfortunately there are no others with millis resolution - only seconds.
      // To make this work, we'll delay reactivation by 1 second so we're guaranteed
      // that the "subscription_reactivated" event has a greater timestamp than the
      // "subscription_changed" event.
      //
      // ChargeBee says they'll work on a fix.
      await Task.Delay(1000);

      ChargeBee.Models.Subscription.Reactivate(hostedPage.Content.Subscription.Id).Request();
      var host = new Uri(hostedPage.Url).Host;
      return Redirect($"https://{host}/pages/v2/{id}/thank_you");
    }

    public virtual IGitHubClient CreateGitHubClient(User user) {
      return GitHubSettings.CreateUserClient(user, Guid.NewGuid());
    }

    private IHttpActionResult BuyPersonal(long actorId, long targetId) {
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

      // Apply a coupon to make up for any unused free trial time that's
      // still remaining.  Don't want to penalize folks that decide to buy
      // before the free trial is up.
      if (sub.Status == ChargeBee.Models.Subscription.StatusEnum.InTrial) {
        var totalDays = (sub.TrialEnd.Value.ToUniversalTime() - DateTime.UtcNow).TotalDays;
        // Always round up to the nearest whole day.
        var daysLeftOnTrial = (int)Math.Min(30, Math.Floor(totalDays + 1));

        pageRequest
          .SubscriptionCoupon($"trial_days_left_{daysLeftOnTrial}")

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
        pageRequest.RedirectUrl($"https://{ApiHostname}/billing/reactivate");
      }

      var result = pageRequest.Request().HostedPage;

      return Redirect(result.Url);
    }

    private async Task<IHttpActionResult> BuyOrganization(long actorId, long targetId, Account targetAccount) {
      var user = await Context.Users.SingleAsync(x => x.Id == actorId);

      var ghc = CreateGitHubClient(user);
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
          .RedirectUrl($"https://{ApiHostname}/billing/reactivate")
          .Request().HostedPage;

        return Redirect(result.Url);
      } else {
        var checkoutRequest = HostedPage.CheckoutNew()
       .CustomerId($"org-{targetId}")
       .CustomerEmail(primaryEmail.Email)
       .SubscriptionPlanId("organization")
       .Param("cf_github_username", ghcOrg.Login)
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
        return BuyPersonal(actorId, targetId);
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
  }
}
