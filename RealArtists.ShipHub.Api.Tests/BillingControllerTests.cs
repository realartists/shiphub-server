namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using System.Web.Http.Results;
  using Common.DataModel;
  using Controllers;
  using Filters;
  using Microsoft.QualityTools.Testing.Fakes;
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

        var result = (OkNegotiatedContentResult<List<BillingAccountRow>>)(await controller.Accounts());
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

    [Test]
    public Task BuyMakesChargeBeePageForCancelledUser() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "cancelled",
        trialEndIfAny: null,
        expectCoupon: null,
        expectTrialToEndImmediately: false
        );
    }

    [Test]
    public Task CouponGivesCreditForRemainingTrialDays() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "in_trial",
        // Pretend trial ends in 7 days, so we should get the 7 day coupon
        trialEndIfAny: DateTimeOffset.UtcNow.AddDays(7),
        expectCoupon: "trial_days_left_7",
        expectTrialToEndImmediately: true
        );
    }

    [Test]
    public Task CouponAlwaysRoundsUpToAWholeDay() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "in_trial",
        // Trial ends in only 6 hours, but we still want to give the user
        // a 1 day coupon.
        trialEndIfAny: DateTimeOffset.UtcNow.AddHours(6),
        expectCoupon: "trial_days_left_1",
        expectTrialToEndImmediately: true
        );
    }

    [Test]
    public Task CouponIsNeverForMoreThan30Days() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "in_trial",
        // Even if you have more than 30 days left in your trial,
        // just use the 30 day coupon.  It's good for 100% off.
        trialEndIfAny: DateTimeOffset.UtcNow.AddDays(31),
        expectCoupon: "trial_days_left_30",
        expectTrialToEndImmediately: true
        );
    }

    public async Task BuyEndpointRedirectsToChargeBeeHelper(
      string existingState,
      DateTimeOffset? trialEndIfAny,
      string expectCoupon,
      bool expectTrialToEndImmediately
      ) {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        user.Token = Guid.NewGuid().ToString();
        await context.SaveChangesAsync();

        using (ShimsContext.Create()) {
          ChargeBeeTestUtil.ShimChargeBeeWebApi((string method, string path, Dictionary<string, string> data) => {
            if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
              Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
              Assert.AreEqual("personal", data["plan_id[is]"]);

              // Pretend we find an existing subscription
              return new {
                list = new object[] {
                  new {
                    subscription = new {
                      id = "existing-sub-id",
                      status = existingState,
                      trial_end = trialEndIfAny?.ToUnixTimeSeconds(),
                    },
                  },
                },
                next_offset = null as string,
              };
            } else if (method.Equals("POST") && path.Equals("/api/v2/hosted_pages/checkout_existing")) {
              if (expectCoupon != null) {
                Assert.AreEqual(expectCoupon, data["subscription[coupon]"]);
              } else {
                Assert.IsFalse(data.ContainsKey("subscription[coupon]"));
              }

              if (expectTrialToEndImmediately) {
                Assert.AreEqual("0", data["subscription[trial_end]"]);
              } else {
                Assert.IsFalse(data.ContainsKey("subscription[trial_end]"));
              }

              return new {
                hosted_page = new {
                  id = "hosted-page-id",
                  url = "https://realartists-test.chargebee.com/some/path/123",
                },
              };
            } else {
              Assert.Fail($"Unexpected {method} to {path}");
              return null;
            }
          });

          var controller = new BillingController();
          var response = controller.Buy(user.Id, user.Id, BillingController.CreateSignature(user.Id, user.Id));
          Assert.IsInstanceOf<RedirectResult>(response);
          Assert.AreEqual("https://realartists-test.chargebee.com/some/path/123", ((RedirectResult)response).Location.AbsoluteUri);
        }
      }
    }

    [Test]
    public async Task ManageEndpointRedirectsToChargeBeePageForPersonal() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        user.Token = Guid.NewGuid().ToString();
        await context.SaveChangesAsync();

        using (ShimsContext.Create()) {
          ChargeBeeTestUtil.ShimChargeBeeWebApi((string method, string path, Dictionary<string, string> data) => {
            if (method.Equals("POST") && path.Equals("/api/v2/portal_sessions")) {
              Assert.AreEqual($"user-{user.Id}", data["customer[id]"]);

              return new {
                portal_session = new {
                  access_url = "https://realartists-test.chargebee.com/some/portal/path/123",
                },
              };
            } else {
              Assert.Fail($"Unexpected {method} to {path}");
              return null;
            }
          });

          var controller = new BillingController();
          var response = await controller.Manage(user.Id, user.Id, BillingController.CreateSignature(user.Id, user.Id));
          Assert.IsInstanceOf<RedirectResult>(response);
          Assert.AreEqual("https://realartists-test.chargebee.com/some/portal/path/123", ((RedirectResult)response).Location.AbsoluteUri);
        }
      }
    }

    [Test]
    public async Task ManageEndpointRedirectsToChargeBeePageForOrganization() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        using (ShimsContext.Create()) {
          ChargeBeeTestUtil.ShimChargeBeeWebApi((string method, string path, Dictionary<string, string> data) => {
            if (method.Equals("POST") && path.Equals("/api/v2/portal_sessions")) {
              Assert.AreEqual($"org-{org.Id}", data["customer[id]"]);

              return new {
                portal_session = new {
                  access_url = "https://realartists-test.chargebee.com/some/portal/path/123",
                },
              };
            } else {
              Assert.Fail($"Unexpected {method} to {path}");
              return null;
            }
          });

          var controller = new BillingController();
          var response = await controller.Manage(user.Id, org.Id, BillingController.CreateSignature(user.Id, org.Id));
          Assert.IsInstanceOf<RedirectResult>(response);
          Assert.AreEqual("https://realartists-test.chargebee.com/some/portal/path/123", ((RedirectResult)response).Location.AbsoluteUri);
        }
      }
    }
  }
}
