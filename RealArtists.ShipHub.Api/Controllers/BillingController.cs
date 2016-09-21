namespace RealArtists.ShipHub.Api.Controllers {
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
  using Common.DataModel;
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

      var apiHostname = CloudConfigurationManager.GetSetting("ApiHostname");
      if (apiHostname == null) {
        throw new ApplicationException("ApiHostname not specified in configuration.");
      }

      var result = combined
       .Select(x => {
          var hasSubscription = x.Subscription.State == SubscriptionState.Subscribed;
          var signature = CreateSignature(principal.UserId, x.Id);
          var actionUrl = $"https://{apiHostname}/billing/{(hasSubscription ? "manage" : "buy")}/{principal.UserId}/{x.Id}/{signature}";

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
    [Route("buy/{actorId}/{targetId}/{signature}")]
    public IHttpActionResult Buy(long actorId, long targetId, string signature) {

      if (!CreateSignature(actorId, targetId).Equals(signature)) {
        return BadRequest("Signature does not match.");
      }

      if (actorId != targetId) {
        return BadRequest("Cannot purchase organization subscriptions yet.");
      }

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
        .SubscriptionPlanId("personal");

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
      }

      var result = pageRequest.Request().HostedPage;

      return Redirect(result.Url);
    }
  }
}
