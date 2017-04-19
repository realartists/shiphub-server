namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http.Results;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Controllers;
  using Filters;
  using Mixpanel;
  using Moq;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
  using NUnit.Framework;
  using QueueClient;

  [TestFixture]
  [AutoRollback]
  public class BillingControllerTests {
    private static IShipHubConfiguration Configuration {
      get {
        var config = new Mock<IShipHubConfiguration>();
        config.Setup(x => x.ApiHostName).Returns("api.realartists.com");
        config.Setup(x => x.WebsiteHostName).Returns("www.realartists.com");
        return config.Object;
      }
    }

    [Test]
    public async Task CanGetAccounts() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org1 = TestUtil.MakeTestOrg(context, 6001, "myorg1");
        var org2 = TestUtil.MakeTestOrg(context, 6002, "myorg2");
        var org3 = TestUtil.MakeTestOrg(context, 6003, "myorg3");
        var org1Repo = TestUtil.MakeTestRepo(context, org1.Id, 2001, "org1repo");
        var org2Repo = TestUtil.MakeTestRepo(context, org2.Id, 2002, "org2repo");
        var org3Repo = TestUtil.MakeTestRepo(context, org3.Id, 2003, "org3repo");

        await context.SetAccountLinkedRepositories(user.Id, new[] {
          Tuple.Create(org1Repo.Id, false),
          Tuple.Create(org2Repo.Id, false),
        });

        await context.SetUserOrganizations(user.Id, new[] { org1.Id, org2.Id, org3.Id });

        context.Subscriptions.AddRange(new[] {
          new Subscription() {
            AccountId = user.Id,
            State = SubscriptionState.InTrial,
            Version = 0,
          },
          new Subscription() {
            AccountId = org1.Id,
            State = SubscriptionState.Subscribed,
            Version = 0,
          },
          // Subscription info is intentionally omitted for org2; we should
          // not see org2 in the results because it's subscription info has
          // not been fetched from ChargeBee yet.
          new Subscription() {
            AccountId = org3.Id,
            State = SubscriptionState.NotSubscribed,
            Version = 0,
          }
        });

        await context.SaveChangesAsync();

        var controller = new BillingController(Configuration, null, null, null, null);
        controller.RequestContext.Principal = new ShipHubPrincipal(user.Id, user.Login);

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
    public async Task AccountsReturnsEmptyListWhenThereIsNoSubscriptionInfo() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org1 = TestUtil.MakeTestOrg(context, 6001, "myorg1");
        var org2 = TestUtil.MakeTestOrg(context, 6002, "myorg2");
        await context.SetUserOrganizations(user.Id, new[] { org1.Id, org2.Id });
        await context.SaveChangesAsync();

        var controller = new BillingController(Configuration, null, null, null, null);
        controller.RequestContext.Principal = new ShipHubPrincipal(user.Id, user.Login);

        var result = (OkNegotiatedContentResult<List<BillingAccountRow>>)await controller.Accounts();
        Assert.AreEqual(0, result.Content.Count);
      }
    }

    [Test]
    public Task BuyMakesChargeBeePageForCancelledUser() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "cancelled",
        trialEndIfAny: null,
        expectCoupon: null,
        expectTrialToEndImmediately: false,
        expectNeedsReactivation: true
        );
    }

    [Test]
    public Task BuyEndpointSendsMixpanelEventIfAnalyticsIdIsSet() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "cancelled",
        trialEndIfAny: null,
        expectCoupon: null,
        expectTrialToEndImmediately: false,
        expectNeedsReactivation: true,
        analyticsId: "someAnalyticsId"
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
    public Task CouponGivesCreditForRemainingTrialDaysFor14DayTrial() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "in_trial",
        // Pretend trial ends in 7 days, so we should get the 7 day coupon
        trialEndIfAny: DateTimeOffset.UtcNow.AddDays(7),
        expectCoupon: "trial_days_left_7_of_14",
        expectTrialToEndImmediately: true,
        subscriptionMetaData: new ChargeBeePersonalSubscriptionMetadata() {
          TrialPeriodDays = 14,
        }
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
    public Task CouponIsNeverForMoreThan30DaysFor30DayTrial() {
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
    public Task CouponIsNeverForMoreThan14DaysFor14DayTrial() {
      return BuyEndpointRedirectsToChargeBeeHelper(
        existingState: "in_trial",
        // Even if you have more than 30 days left in your trial,
        // just use the 30 day coupon.  It's good for 100% off.
        trialEndIfAny: DateTimeOffset.UtcNow.AddDays(31),
        expectCoupon: "trial_days_left_14_of_14",
        expectTrialToEndImmediately: true,
        subscriptionMetaData: new ChargeBeePersonalSubscriptionMetadata() {
          TrialPeriodDays = 14,
        }
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
        expectNeedsReactivation: true,
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
        expectNeedsReactivation: true,
        orgIsPaid: true
        );
    }

    public async Task BuyEndpointRedirectsToChargeBeeHelper(
      string existingState,
      DateTimeOffset? trialEndIfAny,
      string expectCoupon,
      bool expectTrialToEndImmediately,
      bool expectNeedsReactivation = false,
      bool orgIsPaid = false,
      string existingCouponId = null,
      string analyticsId = null,
      ChargeBeePersonalSubscriptionMetadata subscriptionMetaData = null
      ) {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        await context.SetOrganizationAdmins(org.Id, new[] { user.Id });

        context.Subscriptions.Add(new Subscription() {
          AccountId = org.Id,
          State = orgIsPaid ? SubscriptionState.Subscribed : SubscriptionState.NotSubscribed,
        });

        await context.SaveChangesAsync();

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
            Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
            Assert.AreEqual("personal", data["plan_id[is]"]);

            var coupons = (existingCouponId == null) ?
              null :
              new[] {
                new {
                  coupon_id = existingCouponId,
                },
              };

            object subscription = null;

            // If meta_data isn't non-null, don't send the field at all.  ChargeBee's
            // library will choke if meta_data = null.
            if (subscriptionMetaData != null) {
              subscription = new {
                id = "existing-sub-id",
                status = existingState,
                trial_end = trialEndIfAny?.ToUnixTimeSeconds(),
                coupons = coupons,
                meta_data = JToken.FromObject(subscriptionMetaData, GitHubSerialization.JsonSerializer),
              };
            } else {
              subscription = new {
                id = "existing-sub-id",
                status = existingState,
                trial_end = trialEndIfAny?.ToUnixTimeSeconds(),
                coupons = coupons,
              };
            }

            return new {
              list = new object[] {
                new {
                  subscription = subscription,
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

            Assert.AreEqual("/billing/buy/finish", new Uri(data["redirect_url"]).AbsolutePath, "should always bounce to finish page");

            var passThruContent = JsonConvert.DeserializeObject<BuyPassThruContent>(data["pass_thru_content"]);
            if (expectNeedsReactivation) {
              Assert.True(passThruContent.NeedsReactivation, "should have set NeedsReactivation");
            } else {
              Assert.False(passThruContent.NeedsReactivation, "should not have set NeedsReactivation");
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

        var mixpanelEvents = new List<Tuple<string, string, Dictionary<string, object>>>();
        var mockMixpanelClient = new Mock<IMixpanelClient>();
        mockMixpanelClient
          .Setup(x => x.TrackAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<object>()))
          .Callback((string name, object distinctId, object properties) => {
            var propDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(properties));
            mixpanelEvents.Add(Tuple.Create(name, distinctId.ToString(), propDict));
          })
          .ReturnsAsync(true);

        var controller = new BillingController(Configuration, null, api, null, mockMixpanelClient.Object);
        var response = await controller.Buy(user.Id, user.Id, BillingController.CreateSignature(user.Id, user.Id), analyticsId);
        Assert.IsInstanceOf<RedirectResult>(response);
        Assert.AreEqual("https://realartists-test.chargebee.com/some/path/123", ((RedirectResult)response).Location.AbsoluteUri);

        if (analyticsId != null) {
          var mixpanelEvent = mixpanelEvents.Single();
          Assert.AreEqual("Redirect to ChargeBee", mixpanelEvent.Item1);
          Assert.AreEqual("someAnalyticsId", mixpanelEvent.Item2);
          CollectionAssert.AreEquivalent(
            new Dictionary<string, object>() {
              { "_github_login", user.Login },
              { "_github_id", user.Id }
            },
            mixpanelEvent.Item3);
        } else {
          Assert.AreEqual(0, mixpanelEvents.Count, "should not have logged to mixpanel since analytics id was null");
        }
      }
    }

    [Test]
    public async Task ManageEndpointRedirectsToChargeBeePageForPersonal() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        await context.SaveChangesAsync();

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
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

        var controller = new BillingController(Configuration, null, api, null, null);
        var response = await controller.Manage(user.Id, user.Id, BillingController.CreateSignature(user.Id, user.Id));
        Assert.IsInstanceOf<RedirectResult>(response);
        Assert.AreEqual("https://realartists-test.chargebee.com/some/portal/path/123", ((RedirectResult)response).Location.AbsoluteUri);
      }
    }

    [Test]
    public async Task ManageEndpointRedirectsToChargeBeePageForOrganization() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
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

        var controller = new BillingController(Configuration, null, api, null, null);
        var response = await controller.Manage(user.Id, org.Id, BillingController.CreateSignature(user.Id, org.Id));
        Assert.IsInstanceOf<RedirectResult>(response);
        Assert.AreEqual("https://realartists-test.chargebee.com/some/portal/path/123", ((RedirectResult)response).Location.AbsoluteUri);
      }
    }

    [Test]
    public async Task BuyForOrganizationDoesCheckoutNewForNewCustomer() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        var org = TestUtil.MakeTestOrg(context, 6001, "pureimaginary");
        await context.SaveChangesAsync();

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>(), It.IsAny<RequestPriority>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Name = "Aroon Pahwa",
              Login = "aroon",
            },
          });
        mockClient
          .Setup(x => x.UserEmails(It.IsAny<GitHubCacheDetails>(), It.IsAny<RequestPriority>()))
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
          .Setup(x => x.Organization(It.IsAny<string>(), It.IsAny<GitHubCacheDetails>(), It.IsAny<RequestPriority>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = 6001,
              Login = "pureimaginary",
              Name = "Pure Imaginary LLC",
            },
          });

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
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
            Assert.AreEqual("/billing/buy/finish", new Uri(data["redirect_url"]).AbsolutePath);

            var expectedPassThruContent = JsonConvert.SerializeObject(new BuyPassThruContent() {
              ActorId = user.Id,
              ActorLogin = user.Login,
              AnalyticsId = "someAnalyticsId",
              NeedsReactivation = false,
            });
            Assert.AreEqual(expectedPassThruContent, data["pass_thru_content"]);

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

        var mockMixpanelClient = new Mock<IMixpanelClient>();
        mockMixpanelClient.Setup(x => x.TrackAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<object>())).ReturnsAsync(true);

        var mock = new Mock<BillingController>(Configuration, null, api, null, mockMixpanelClient.Object) { CallBase = true };
        mock
          .Setup(x => x.CreateGitHubActor(It.IsAny<long>()))
          .Returns((long forUserId) => {
            Assert.AreEqual(user.Id, forUserId);
            return mockClient.Object;
          });

        var response = await mock.Object.Buy(user.Id, org.Id, BillingController.CreateSignature(user.Id, org.Id), "someAnalyticsId");
        Assert.IsInstanceOf<RedirectResult>(response);
        Assert.AreEqual("https://realartists-test.chargebee.com/some/path/123", ((RedirectResult)response).Location.AbsoluteUri);
      }
    }

    [Test]
    public async Task BuyForOrganizationDoesCheckoutExistingForOldCustomer() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        var org = TestUtil.MakeTestOrg(context, 6001, "pureimaginary");
        await context.SaveChangesAsync();

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>(), It.IsAny<RequestPriority>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Name = "Aroon Pahwa",
              Login = "aroon",
            },
          });
        mockClient
          .Setup(x => x.UserEmails(It.IsAny<GitHubCacheDetails>(), It.IsAny<RequestPriority>()))
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
          .Setup(x => x.Organization(It.IsAny<string>(), It.IsAny<GitHubCacheDetails>(), It.IsAny<RequestPriority>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = 6001,
              Login = "pureimaginary",
              Name = "Pure Imaginary LLC",
            },
          });

        var doesCustomerUpdate = false;
        var doesCheckoutExisting = false;

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
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
            Assert.AreEqual("/billing/buy/finish", new Uri(data["redirect_url"]).AbsolutePath);

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

        var mock = new Mock<BillingController>(Configuration, null, api, null, null) { CallBase = true };
        mock
          .Setup(x => x.CreateGitHubActor(It.IsAny<long>()))
          .Returns((long forUserId) => {
            Assert.AreEqual(user.Id, forUserId);
            return mockClient.Object;
          });

        var response = await mock.Object.Buy(user.Id, org.Id, BillingController.CreateSignature(user.Id, org.Id));
        Assert.IsInstanceOf<RedirectResult>(response);
        Assert.AreEqual("https://realartists-test.chargebee.com/some/path/123", ((RedirectResult)response).Location.AbsoluteUri);

        Assert.IsTrue(doesCustomerUpdate);
        Assert.IsTrue(doesCheckoutExisting);
      }
    }

    [Test]
    public async Task BuyFinishEndpointCanReactivateSubscription() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        var org = TestUtil.MakeTestOrg(context, 6001, "pureimaginary");
        await context.SaveChangesAsync();

        var doesReactivate = false;

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path == "/api/v2/hosted_pages/someHostedPageId") {
            return new {
              hosted_page = new {
                state = "succeeded",
                url = "https://realartists-test.chargebee.com/pages/v2/someHostedPageId/checkout",
                content = new {
                  subscription = new {
                    id = "someSubId",
                    customer_id = $"user-{user.Id}",
                    plan_id = "someplan",
                    plan_unit_price = 900,
                    resource_version = 999,
                  }
                },
                pass_thru_content = JsonConvert.SerializeObject(new BuyPassThruContent() {
                  NeedsReactivation = true,
                }),
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

        var queueClientMock = new Mock<IShipHubQueueClient>();

        var controller = new BillingController(Configuration, null, api, queueClientMock.Object, null);
        var response = await controller.BuyFinish("someHostedPageId", "succeeded");
        Assert.IsInstanceOf<RedirectResult>(response);
        Assert.IsTrue(doesReactivate);
      }
    }

    private async Task<IChangeSummary> BuyFinishEndpointUpdatesSubscriptionStateHelper(ShipHubContext context, string customerId) {
      var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
        if (method.Equals("GET") && path == "/api/v2/hosted_pages/someHostedPageId") {
          return new {
            hosted_page = new {
              state = "succeeded",
              url = "https://realartists-test.chargebee.com/pages/v2/someHostedPageId/checkout",
              content = new {
                subscription = new {
                  id = "someSubId",
                  plan_id = "someplan",
                  plan_unit_price = 900,
                  customer_id = customerId,
                  resource_version = 1234,
                },
                customer = new {
                  id = customerId,
                  cf_github_username = "somelogin",
                },
              },
              pass_thru_content = JsonConvert.SerializeObject(new BuyPassThruContent() {
                NeedsReactivation = false,
              }),
            },
          };
        } else {
          Assert.Fail($"Unexpected {method} to {path}");
          return null;
        }
      });

      IChangeSummary changes = null;

      var queueClientMock = new Mock<IShipHubQueueClient>();
      queueClientMock.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns((IChangeSummary c) => {
          changes = c;
          return Task.CompletedTask;
        });

      var controller = new BillingController(Configuration, null, api, queueClientMock.Object, null);
      var response = await controller.BuyFinish("someHostedPageId", "succeeded");
      Assert.IsInstanceOf<RedirectResult>(response);

      var redirectUrl = ((RedirectResult)response).Location.AbsoluteUri;
      var redirectUrlParts = redirectUrl.Split('#');
      Assert.AreEqual($"https://{Configuration.WebsiteHostName}/signup-thankyou.html", redirectUrlParts[0]);

      return changes;
    }

    [Test]
    public async Task BuyFinishEndpointUpdatesSubscriptionStateForUser() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.InTrial,
          TrialEndDate = DateTimeOffset.UtcNow.AddDays(30),
          Version = 1,
        });
        await context.SaveChangesAsync();

        var changes = await BuyFinishEndpointUpdatesSubscriptionStateHelper(context, $"user-{user.Id}");

        var sub = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == user.Id);
        context.Entry(sub).Reload();
        Assert.NotNull(sub, "should have found subscription");
        Assert.AreEqual(SubscriptionState.Subscribed, sub.State);
        Assert.Null(sub.TrialEndDate);
        Assert.AreEqual(1234, sub.Version);

        Assert.NotNull(changes, "should have sent notification about changes");
        Assert.AreEqual(new long[] { user.Id }, changes.Users.ToArray());
      }
    }

    [Test]
    public async Task BuyFinishEndpointSendsMixpanelEventWhenAnalyticsIdIsSet() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        var org = TestUtil.MakeTestOrg(context);
        context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.NotSubscribed,
          Version = 1,
        });
        await context.SaveChangesAsync();

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path == "/api/v2/hosted_pages/someHostedPageId") {
            return new {
              hosted_page = new {
                state = "succeeded",
                url = "https://realartists-test.chargebee.com/pages/v2/someHostedPageId/checkout",
                content = new {
                  subscription = new {
                    id = "someSubId",
                    plan_id = "organization",
                    plan_unit_price = 2500,
                    customer_id = $"org-{org.Id}",
                    resource_version = 1234,
                  },
                  customer = new {
                    id = $"org-{org.Id}",
                    cf_github_username = org.Login,
                  },
                },
                pass_thru_content = JsonConvert.SerializeObject(new BuyPassThruContent() {
                  AnalyticsId = "someAnalyticsId",
                  NeedsReactivation = false,
                  ActorId = user.Id,
                  ActorLogin = user.Login,
                }),
              },
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        var queueClientMock = new Mock<IShipHubQueueClient>();
        queueClientMock.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>())).Returns(Task.CompletedTask);

        var mixpanelEvents = new List<Tuple<string, string, Dictionary<string, object>>>();
        var mockMixpanelClient = new Mock<IMixpanelClient>();
        mockMixpanelClient
          .Setup(x => x.TrackAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<object>()))
          .Callback((string name, object distinctId, object properties) => {
            var propDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(properties));
            mixpanelEvents.Add(Tuple.Create(name, distinctId.ToString(), propDict));
          })
          .ReturnsAsync(true);

        var controller = new BillingController(Configuration, null, api, queueClientMock.Object, mockMixpanelClient.Object);
        var response = await controller.BuyFinish("someHostedPageId", "succeeded");

        Assert.AreEqual(1, mixpanelEvents.Count, "should have sent 1 event");
        var purchaseEvent = mixpanelEvents[0];
        Assert.AreEqual("Purchased", purchaseEvent.Item1);
        Assert.AreEqual("someAnalyticsId", purchaseEvent.Item2);
        CollectionAssert.AreEquivalent(new Dictionary<string, object>() {
          { "plan", "organization" },
          { "customer_id", $"org-{org.Id}" },
          { "_github_login", user.Login },
          { "_github_id", user.Id },
        }, purchaseEvent.Item3);
      }
    }

    [Test]
    public async Task BuyFinishEndpointSendsHashParamsToThankYouPage() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        var org = TestUtil.MakeTestOrg(context);
        context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.NotSubscribed,
          Version = 1,
        });
        await context.SaveChangesAsync();

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path == "/api/v2/hosted_pages/someHostedPageId") {
            return new {
              hosted_page = new {
                state = "succeeded",
                url = "https://realartists-test.chargebee.com/pages/v2/someHostedPageId/checkout",
                content = new {
                  subscription = new {
                    id = "someSubId",
                    plan_id = "organization",
                    plan_unit_price = 2500,
                    customer_id = $"org-{org.Id}",
                    resource_version = 1234,
                  },
                  customer = new {
                    id = $"org-{org.Id}",
                    cf_github_username = org.Login,
                  },
                },
                pass_thru_content = JsonConvert.SerializeObject(new BuyPassThruContent() {
                  AnalyticsId = "someAnalyticsId",
                  NeedsReactivation = false,
                  ActorId = user.Id,
                  ActorLogin = user.Login,
                }),
              },
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        var queueClientMock = new Mock<IShipHubQueueClient>();
        queueClientMock.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>())).Returns(Task.CompletedTask);

        var mockMixpanelClient = new Mock<IMixpanelClient>();
        mockMixpanelClient
          .Setup(x => x.TrackAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<object>()))
          .ReturnsAsync(true);

        var controller = new BillingController(Configuration, null, api, queueClientMock.Object, mockMixpanelClient.Object);
        var response = await controller.BuyFinish("someHostedPageId", "succeeded");
        var redirectResponse = (RedirectResult)response;

        var hashParamsBase64 = WebUtility.UrlDecode(((RedirectResult)response).Location.Fragment.Substring(1));
        var hashParams = JsonConvert.DeserializeObject<ThankYouPageHashParams>(
          Encoding.UTF8.GetString(Convert.FromBase64String(hashParamsBase64)),
          GitHubSerialization.JsonSerializerSettings);

        Assert.AreEqual(25, hashParams.Value);
        Assert.AreEqual("organization", hashParams.PlanId);
      }
    }

    [Test]
    public async Task BuyFinishEndpointUpdatesSubscriptionStateForOrg() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context, 3001, "aroon");
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        var changes = await BuyFinishEndpointUpdatesSubscriptionStateHelper(context, $"org-{org.Id}");

        var sub = await context.Subscriptions.SingleOrDefaultAsync(x => x.AccountId == org.Id);
        Assert.NotNull(sub, "should have found subscription");
        Assert.AreEqual(SubscriptionState.Subscribed, sub.State);
        Assert.Null(sub.TrialEndDate);
        Assert.AreEqual(1234, sub.Version);

        Assert.NotNull(changes, "should have sent notification about changes");
        Assert.AreEqual(new long[] { org.Id }, changes.Organizations.ToArray());
      }
    }

    [Test]
    public async Task UpdatePaymentEndpointRedirectsToChargeBeePage() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
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

        var controller = new BillingController(Configuration, null, api, null, null);
        var response = await controller.UpdatePaymentMethod(org.Id, BillingController.CreateSignature(org.Id, org.Id));
        Assert.IsInstanceOf<RedirectResult>(response);
        Assert.AreEqual("https://realartists-test.chargebee.com/some/page/path/123", ((RedirectResult)response).Location.AbsoluteUri);
      }
    }
  }
}
