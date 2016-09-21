namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net.Http;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http;
  using System.Web.Http.Controllers;
  using System.Web.Http.Hosting;
  using System.Web.Http.Results;
  using System.Web.Http.Routing;
  using Common.DataModel;
  using Controllers;
  using Filters;
  using NUnit.Framework;

  [TestFixture]
  [AutoRollback]
  public class BillingControllerTests {

    [Test]
    public async Task CanGetAccounts() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        user.Token = Guid.NewGuid().ToString();
        var org1 = TestUtil.MakeTestOrg(context, 6001, "myorg1");
        var org2 = TestUtil.MakeTestOrg(context, 6002, "myorg2");

        await context.SetOrganizationUsers(org1.Id, new[] {
          Tuple.Create(user.Id, false),
        });
        await context.SetOrganizationUsers(org2.Id, new[] {
          Tuple.Create(user.Id, false),
        });

        context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.InTrial,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = org1.Id,
          State = SubscriptionState.Subscribed,
        });

        await context.SaveChangesAsync();

        var controller = new BillingController();
        controller.RequestContext.Principal = new ShipHubPrincipal(user.Id, user.Login, user.Token);

        var result = (OkNegotiatedContentResult<List<BillingController.AccountRow>>)(await controller.Accounts());
        Assert.AreEqual(2, result.Content.Count);
        Assert.AreEqual(user.Id, result.Content[0].Account.Identifier);
        Assert.AreEqual(user.Login, result.Content[0].Account.Login);
        Assert.AreEqual("user", result.Content[0].Account.Type);
        Assert.AreEqual(false, result.Content[0].Subscribed,
          "not subscribed since user is in trial");
        Assert.AreEqual(org1.Id, result.Content[1].Account.Identifier);
        Assert.AreEqual(true, result.Content[1].Subscribed,
          "should show subscribed since we're in a paid subscription");
        Assert.AreEqual(true, result.Content[1].CanEdit,
          "can edit since it has a subscription");
        Assert.AreEqual(org1.Login, result.Content[1].Account.Login);
        Assert.AreEqual("organization", result.Content[1].Account.Type);
      }
    }

    [Test]
    public async Task AccountsReturnsErrorWithNoSubscriptionInfo() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        user.Token = Guid.NewGuid().ToString();
        var org1 = TestUtil.MakeTestOrg(context, 6001, "myorg1");
        var org2 = TestUtil.MakeTestOrg(context, 6002, "myorg2");

        await context.SetOrganizationUsers(org1.Id, new[] {
          Tuple.Create(user.Id, false),
        });
        await context.SetOrganizationUsers(org2.Id, new[] {
          Tuple.Create(user.Id, false),
        });

        await context.SaveChangesAsync();

        var controller = new BillingController();
        controller.RequestContext.Principal = new ShipHubPrincipal(user.Id, user.Login, user.Token);

        var result = await controller.Accounts();
        Assert.IsInstanceOf<NegotiatedContentResult<Dictionary<string, string>>>(result);
      }
    }
  }
}
