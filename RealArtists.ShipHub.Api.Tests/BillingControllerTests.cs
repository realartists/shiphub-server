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

        var result = (OkNegotiatedContentResult<List<BillingController.Account>>)(await controller.Accounts());
        Assert.AreEqual(2, result.Content.Count);
        Assert.AreEqual(user.Id, result.Content[0].Id);
        Assert.AreEqual(BillingController.AccountAction.Purchase, result.Content[0].Action,
          "should be purchase since user is in trial");
        Assert.AreEqual(org1.Id, result.Content[1].Id);
        Assert.AreEqual(BillingController.AccountAction.Manage, result.Content[1].Action,
          "should be manage since we have a subscription");
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
