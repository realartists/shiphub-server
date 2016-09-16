namespace RealArtists.ShipHub.Api.Controllers {
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Runtime.Serialization;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common.DataModel;
  using Filters;

  [RoutePrefix("billing")]
  public class BillingController : ShipHubController {

    public enum AccountAction {
      [EnumMember(Value = "manage")]
      Manage,

      [EnumMember(Value = "purchase")]
      Purchase,
    }

    public class Account {
      public long Id { get; set; }
      public AccountAction Action { get; set; }
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
        .Select(x => new Account() {
          Id = x.Id,
          Action = x.Subscription.State == SubscriptionState.Subscribed ? AccountAction.Manage : AccountAction.Purchase,
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
