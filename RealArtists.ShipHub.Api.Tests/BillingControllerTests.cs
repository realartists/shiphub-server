namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using System.Web.Http.Results;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Controllers;
  using Filters;
  using Microsoft.QualityTools.Testing.Fakes;
  using Moq;
  using NUnit.Framework;

  [TestFixture]
  [AutoRollback]
  public class BillingControllerTests {
    private static IShipHubConfiguration Configuration { get; } = new ShipHubCloudConfiguration();

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
          Version = 0,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = org1.Id,
          State = SubscriptionState.Subscribed,
          Version = 0,
        });

        await context.SaveChangesAsync();

        var controller = new BillingController(Configuration, null);
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

        var controller = new BillingController(Configuration, null);
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
        expectTrialToEndImmediately: false,
        expectRedirectToReactivation: true
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

    [Test]
    public Task PersonalPlanIsComplimentaryIfMemberOfPaidOrgWhenInTrial() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "in_trial",
        // Pretend trial ends in 7 days, so we should get the 7 day coupon
        trialEndIfAny: DateTimeOffset.UtcNow.AddDays(7),
        expectCoupon: "member_of_paid_org",
        expectTrialToEndImmediately: true,
        orgIsPaid: true
        );
    }

    [Test]
    public Task WillNotSetComplimentaryCouponIfSubscriptionAlreadyHasTheCouponWhenInTrial() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "in_trial",
        existingCouponId: "member_of_paid_org",
        // Pretend trial ends in 7 days, so we should get the 7 day coupon
        trialEndIfAny: DateTimeOffset.UtcNow.AddDays(7),
        expectCoupon: null,
        expectTrialToEndImmediately: true,
        orgIsPaid: true
        );
    }

    [Test]
    public Task PersonalPlanIsComplimentaryIfMemberOfPaidOrgWhenCancelled() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "cancelled",
        trialEndIfAny: null,
        expectCoupon: "member_of_paid_org",
        expectTrialToEndImmediately: false,
        expectRedirectToReactivation: true,
        orgIsPaid: true
        );
    }

    [Test]
    public Task WillNotSetComplimentaryCouponIfSubscriptionAlreadyHasTheCouponWhenCancelled() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "cancelled",
        existingCouponId: "member_of_paid_org",
        trialEndIfAny: null,
        expectCoupon: null,
        expectTrialToEndImmediately: false,
        expectRedirectToReactivation: true,
        orgIsPaid: true
        );
    }

    public async Task BuyEndpointRedirectsToChargeBeeHelper(
      string existingState,
      DateTimeOffset? trialEndIfAny,
      string expectCoupon,
      bool expectTrialToEndImmediately,
      bool expectRedirectToReactivation = false,
      bool orgIsPaid = false,
      string existingCouponId = null
      ) {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        user.Token = Guid.NewGuid().ToString();
        var org = TestUtil.MakeTestOrg(context);
        await context.SetOrganizationUsers(org.Id, new[] { Tuple.Create(user.Id, true) });

        context.Subscriptions.Add(new Subscription() {
          AccountId = org.Id,
          State = orgIsPaid ? SubscriptionState.Subscribed : SubscriptionState.NotSubscribed,
        });

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
                      coupons = (existingCouponId == null) ?
                        null :
                        new[] {
                          new {
                            coupon_id = existingCouponId,
                          },
                        },
                    },
                  },
                },
                next_offset = null as string,
              };
            } else if (method.Equals("POST") && path.Equals("/api/v2/hosted_pages/checkout_existing")) {
              if (expectCoupon != null) {
                Assert.AreEqual(expectCoupon, data["subscription[coupon]"], "should have set coupon");
              } else {
                Assert.IsFalse(data.ContainsKey("subscription[coupon]"), "should not have applied coupon");
              }

              if (expectTrialToEndImmediately) {
                Assert.AreEqual("0", data["subscription[trial_end]"], "should have set trial to end");
              } else {
                Assert.IsFalse(data.ContainsKey("subscription[trial_end]"), "should not have set trial to end");
              }

              if (expectRedirectToReactivation) {
                Assert.AreEqual("/billing/reactivate", new Uri(data["redirect_url"]).AbsolutePath, "should bounce to reactivate page");
              } else {
                Assert.AreEqual(BillingController.SignupThankYouUrl, data["redirect_url"], "should go to thank you page.");
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

          var controller = new BillingController(Configuration, null);
          var response = await controller.Buy(user.Id, user.Id, BillingController.CreateSignature(user.Id, user.Id));
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

          var controller = new BillingController(Configuration, null);
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

          var controller = new BillingController(Configuration, null);
          var response = await controller.Manage(user.Id, org.Id, BillingController.CreateSignature(user.Id, org.Id));
          Assert.IsInstanceOf<RedirectResult>(response);
          Assert.AreEqual("https://realartists-test.chargebee.com/some/portal/path/123", ((RedirectResult)response).Location.AbsoluteUri);
        }
      }
    }

    [Test]
    public async Task BuyForOrganizationDoesCheckoutNewForNewCustomer() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        user.Token = Guid.NewGuid().ToString();
        var org = TestUtil.MakeTestOrg(context, 6001, "pureimaginary");
        await context.SaveChangesAsync();

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Name = "Aroon Pahwa",
              Login = "aroon",
            },
          });
        mockClient
          .Setup(x => x.UserEmails(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Common.GitHub.Models.UserEmail>>(null) {
            Result = new[] {
              new Common.GitHub.Models.UserEmail() {
                Email = "aroon@pureimaginary.com",
                Primary = true,
                Verified = true,
              },
            }
          });
        mockClient
          .Setup(x => x.Organization(It.IsAny<string>(), It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = 6001,
              Login = "pureimaginary",
              Name = "Pure Imaginary LLC",
            },
          });

        var mock = new Mock<BillingController>(Configuration, null) { CallBase = true };
        mock
          .Setup(x => x.CreateGitHubActor(It.IsAny<User>()))
          .Returns((User forUser) => {
            Assert.AreEqual(user.Id, forUser.Id);
            return mockClient.Object;
          });


        using (ShimsContext.Create()) {
          ChargeBeeTestUtil.ShimChargeBeeWebApi((string method, string path, Dictionary<string, string> data) => {
            if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
              Assert.AreEqual($"org-{org.Id}", data["customer_id[is]"]);
              Assert.AreEqual("organization", data["plan_id[is]"]);

              // Pretend no past subscriptions.
              return new {
                list = new object[0] {
                },
                next_offset = null as string,
              };
            } else if (method.Equals("POST") && path.Equals("/api/v2/hosted_pages/checkout_new")) {
              Assert.AreEqual("organization", data["subscription[plan_id]"]);
              Assert.AreEqual("aroon@pureimaginary.com", data["customer[email]"]);
              Assert.AreEqual("Pure Imaginary LLC", data["customer[company]"]);
              Assert.AreEqual("pureimaginary", data["customer[cf_github_username]"]);

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

          var response = await mock.Object.Buy(user.Id, org.Id, BillingController.CreateSignature(user.Id, org.Id));
          Assert.IsInstanceOf<RedirectResult>(response);
          Assert.AreEqual("https://realartists-test.chargebee.com/some/path/123", ((RedirectResult)response).Location.AbsoluteUri);
        }
      }
    }

    [Test]
    public async Task BuyForOrganizationDoesCheckoutExistingForOldCustomer() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        user.Token = Guid.NewGuid().ToString();
        var org = TestUtil.MakeTestOrg(context, 6001, "pureimaginary");
        await context.SaveChangesAsync();

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Name = "Aroon Pahwa",
              Login = "aroon",
            },
          });
        mockClient
          .Setup(x => x.UserEmails(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Common.GitHub.Models.UserEmail>>(null) {
            Result = new[] {
              new Common.GitHub.Models.UserEmail() {
                Email = "aroon@pureimaginary.com",
                Primary = true,
                Verified = true,
              },
            }
          });
        mockClient
          .Setup(x => x.Organization(It.IsAny<string>(), It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = 6001,
              Login = "pureimaginary",
              Name = "Pure Imaginary LLC",
            },
          });

        var mock = new Mock<BillingController>(Configuration, null) { CallBase = true };
        mock
          .Setup(x => x.CreateGitHubActor(It.IsAny<User>()))
          .Returns((User forUser) => {
            Assert.AreEqual(user.Id, forUser.Id);
            return mockClient.Object;
          });


        using (ShimsContext.Create()) {
          bool doesCustomerUpdate = false;
          bool doesCheckoutExisting = false;

          ChargeBeeTestUtil.ShimChargeBeeWebApi((string method, string path, Dictionary<string, string> data) => {
            if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
              Assert.AreEqual($"org-{org.Id}", data["customer_id[is]"]);
              Assert.AreEqual("organization", data["plan_id[is]"]);

              // Pretend we have an expired subscription.
              return new {
                list = new object[] {
                  new {
                    subscription = new {
                      id = "existing-sub-id",
                      status = "cancelled",
                    },
                  },
                },
                next_offset = null as string,
              };
            } else if (method.Equals("POST") && path.Equals($"/api/v2/customers/org-{org.Id}")) {
              doesCustomerUpdate = true;
              Assert.AreEqual("Aroon", data["first_name"]);
              Assert.AreEqual("Pahwa", data["last_name"]);
              Assert.AreEqual("Pure Imaginary LLC", data["company"]);
              Assert.AreEqual("pureimaginary", data["cf_github_username"]);

              return new {
                customer = new {
                  id = $"org-{org.Id}",
                },
              };
            } else if (method.Equals("POST") && path.Equals("/api/v2/hosted_pages/checkout_existing")) {
              doesCheckoutExisting = true;
              Assert.AreEqual("existing-sub-id", data["subscription[id]"]);
              Assert.AreEqual("organization", data["subscription[plan_id]"]);
              Assert.AreEqual("/billing/reactivate", new Uri(data["redirect_url"]).AbsolutePath);

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

          var response = await mock.Object.Buy(user.Id, org.Id, BillingController.CreateSignature(user.Id, org.Id));
          Assert.IsInstanceOf<RedirectResult>(response);
          Assert.AreEqual("https://realartists-test.chargebee.com/some/path/123", ((RedirectResult)response).Location.AbsoluteUri);

          Assert.IsTrue(doesCustomerUpdate);
          Assert.IsTrue(doesCheckoutExisting);
        }
      }
    }

    [Test]
    public async Task ReactivateEndpointReactivatesAndSaysThankYou() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        user.Token = Guid.NewGuid().ToString();
        var org = TestUtil.MakeTestOrg(context, 6001, "pureimaginary");
        await context.SaveChangesAsync();

        using (ShimsContext.Create()) {
          bool doesReactivate = false;

          ChargeBeeTestUtil.ShimChargeBeeWebApi((string method, string path, Dictionary<string, string> data) => {
            if (method.Equals("GET") && path == "/api/v2/hosted_pages/someHostedPageId") {
              return new {
                hosted_page = new {
                  state = "succeeded",
                  url = "https://realartists-test.chargebee.com/pages/v2/someHostedPageId/checkout",
                  content = new {
                    subscription = new {
                      id = "someSubId",
                    }
                  }
                },
              };
            } else if (method.Equals("POST") && path.Equals($"/api/v2/subscriptions/someSubId/reactivate")) {
              doesReactivate = true;

              return new {
                subscription = new {
                  id = "someSubId",
                },
              };
            } else {
              Assert.Fail($"Unexpected {method} to {path}");
              return null;
            }
          });

          var controller = new BillingController(Configuration, null);
          var response = controller.Reactivate("someHostedPageId", "succeeded");
          Assert.IsInstanceOf<RedirectResult>(response);
          Assert.AreEqual(BillingController.SignupThankYouUrl, ((RedirectResult)response).Location.AbsoluteUri);

          Assert.IsTrue(doesReactivate);
        }
      }
    }

    [Test]
    public async Task UpdatePaymentEndpointRedirectsToChargeBeePage() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        using (ShimsContext.Create()) {
          ChargeBeeTestUtil.ShimChargeBeeWebApi((string method, string path, Dictionary<string, string> data) => {
            if (method.Equals("POST") && path.Equals("/api/v2/hosted_pages/update_payment_method")) {
              Assert.AreEqual($"org-{org.Id}", data["customer[id]"]);

              return new {
                hosted_page = new {
                  url = "https://realartists-test.chargebee.com/some/page/path/123",
                },
              };
            } else {
              Assert.Fail($"Unexpected {method} to {path}");
              return null;
            }
          });

          var controller = new BillingController(Configuration, null);
          var response = await controller.UpdatePaymentMethod(org.Id, BillingController.CreateSignature(org.Id, org.Id));
          Assert.IsInstanceOf<RedirectResult>(response);
          Assert.AreEqual("https://realartists-test.chargebee.com/some/page/path/123", ((RedirectResult)response).Location.AbsoluteUri);
        }
      }
    }
  }
}
