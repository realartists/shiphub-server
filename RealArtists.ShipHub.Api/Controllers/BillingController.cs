namespace RealArtists.ShipHub.Api.Controllers {
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common.DataModel;
  using Filters;

  [RoutePrefix("billing")]
  public class BillingController : ShipHubController {

    public class Account {
      public long Identifier { get; set; }
      public string Login { get; set; }
      public string AvatarUrl { get; set; }
      public string Type { get; set; }
    }

    public class AccountRow {
      public Account Account { get; set; }
      public bool Subscribed { get; set; }
      public bool CanEdit { get; set; }
      public string ActionUrl { get; set; }
      public string[] PricingLines { get; set; }
    }

    private static string[] GetActionLines(Common.DataModel.Account account) {
      if (account.Subscription.State == SubscriptionState.Subscribed) {
        // Should server send the "Already Subscribed" place holder text?
        return null;
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

      var combined = new List<Common.DataModel.Account>();

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
        .Select(x => new AccountRow() {
          Account = new Account() {
            Identifier = x.Id,
            Login = x.Login,
            // TODO: Sync avatars and return real values here.
            AvatarUrl = "https://avatars.githubusercontent.com/u/335107?v=3",
            Type = (x is User) ? "user" : "organization",
          },
          Subscribed = x.Subscription.State == SubscriptionState.Subscribed,
          // TODO: Only allow edits for purchaser or admins.
          CanEdit = x.Subscription.State == SubscriptionState.Subscribed,
          ActionUrl = "https://www.realartists.com",
          PricingLines = GetActionLines(x),
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
  }
}
