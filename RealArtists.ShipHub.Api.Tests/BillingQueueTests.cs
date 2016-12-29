namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common.DataModel;
  using Common.GitHub;
  using RealArtists.ChargeBee;
  using Microsoft.Azure.WebJobs;
  using Moq;
  using NUnit.Framework;
  using QueueClient.Messages;
  using QueueProcessor.Jobs;
  using QueueProcessor.Tracing;

  [TestFixture]
  [AutoRollback]
  public class BillingQueueTests {

    private static BillingQueueHandler CreateHandler(ChargeBeeApi api) {
      return new BillingQueueHandler(null, new DetailedExceptionLogger(), api);
    }

    [Test]
    public async Task WillCreateTrialIfNeeded() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Login = "aroon",
              Name = "Aroon Pahwa",
              Type = Common.GitHub.Models.GitHubAccountType.User,
            }
          });
        mockClient
          .Setup(x => x.UserEmails(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Common.GitHub.Models.UserEmail>>(null) {
            Result = new[] {
              new Common.GitHub.Models.UserEmail() {
                Email = "aroon@pureimaginary.com",
                Primary = true,
                Verified = true,
              }
            },
          });

        bool createdAccount = false;
        bool createdSubscription = false;

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path.Equals("/api/v2/customers")) {
            Assert.AreEqual(new Dictionary<string, string>() { { "id[is]", $"user-{user.Id}" } }, data);
            // Pretend no existing customers found for this id.
            return new {
              list = new object[0],
              next_offset = null as string,
            };
          } else if (method.Equals("POST") && path.Equals("/api/v2/customers")) {
            Assert.AreEqual(
              new Dictionary<string, string> {
                  { "id", $"user-{user.Id}" },
                  { "cf_github_username", "aroon" },
                  { "email", "aroon@pureimaginary.com" },
                  { "first_name", $"Aroon"},
                  { "last_name", $"Pahwa"},
              },
              data);
            createdAccount = true;
            // Fake response for customer creation.
            return new {
              customer = new {
                id = $"user-{user.Id}",
              },
            };
          } else if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
            Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
            Assert.AreEqual("personal", data["plan_id[is]"]);

            // Pretend no existing subscriptions found.
            return new {
              list = new object[0],
              next_offset = null as string,
            };
          } else if (method.Equals("POST") && path.Equals($"/api/v2/customers/user-{user.Id}/subscriptions")) {
            Assert.AreEqual(
              new Dictionary<string, string> {
                      { "plan_id", "personal"},
              },
              data);
            createdSubscription = true;
            // Fake response for creating the subscription.
            return new {
              subscription = new {
                id = "some-sub-id",
                status = "in_trial",
                trial_end = DateTimeOffset.Parse(
                      "10/1/2016 08:00:00 PM +00:00",
                      null,
                      DateTimeStyles.AssumeUniversal).ToUnixTimeSeconds(),
                resource_version = 1234,
              },
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        await CreateHandler(api).GetOrCreatePersonalSubscriptionHelper(new UserIdMessage(user.Id), collectorMock.Object, mockClient.Object, Console.Out);

        var sub = context.Subscriptions.Single(x => x.AccountId == user.Id);
        Assert.AreEqual(SubscriptionState.InTrial, sub.State);
        Assert.AreEqual(DateTimeOffset.Parse(
                        "10/1/2016 08:00:00 PM +00:00",
                        null,
                        DateTimeStyles.AssumeUniversal),
                        sub.TrialEndDate);
        Assert.IsTrue(createdAccount);
        Assert.IsTrue(createdSubscription);

        Assert.AreEqual(
          new long[] { user.Id },
          changeMessages.FirstOrDefault()?.Users.OrderBy(x => x).ToArray(),
          "should notify that this user changed.");
      }
    }

    [Test]
    public async Task WillUpdateSubscriptionStateFromChargeBee() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var subscription = context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.InTrial,
          TrialEndDate = DateTimeOffset.Parse("1/1/2017"),
          Version = 0,
        });
        await context.SaveChangesAsync();

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Login = "aroon",
              Name = "Aroon Pahwa",
              Type = Common.GitHub.Models.GitHubAccountType.User,
            }
          });

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path.Equals("/api/v2/customers")) {
            Assert.AreEqual(new Dictionary<string, string>() { { "id[is]", $"user-{user.Id}" } }, data);
            // Pretend no existing customers found for this id.
            return new {
              list = new object[] {
                  new {
                    customer = new {
                      id = $"user-{user.Id}",
                    },
                  },
                },
              next_offset = null as string,
            };
          } else if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
            Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
            Assert.AreEqual("personal", data["plan_id[is]"]);

            return new {
              list = new object[] {
                  new {
                    subscription = new {
                      id = "existing-sub-id",
                      status = "active",
                      resource_version = 1234,
                    },
                  },
                },
              next_offset = null as string,
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        await CreateHandler(api).GetOrCreatePersonalSubscriptionHelper(new UserIdMessage(user.Id), collectorMock.Object, mockClient.Object, Console.Out);

        context.Entry(subscription).Reload();
        Assert.AreEqual(SubscriptionState.Subscribed, subscription.State,
          "should change to subscribed because chargebee says subscription is active.");
        Assert.IsNull(subscription.TrialEndDate);
        Assert.AreEqual(1234, subscription.Version);

        Assert.AreEqual(
          new long[] { user.Id },
          changeMessages.FirstOrDefault()?.Users.OrderBy(x => x).ToArray(),
          "should notify that this user changed.");
      }
    }

    [Test]
    public async Task WillNotSendChangeMessageIfSubscriptionStateIsUnchanged() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var subscription = context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.Subscribed,
          Version = 1234,
        });
        await context.SaveChangesAsync();

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Login = "aroon",
              Name = "Aroon Pahwa",
              Type = Common.GitHub.Models.GitHubAccountType.User,
            }
          });

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path.Equals("/api/v2/customers")) {
            Assert.AreEqual(new Dictionary<string, string>() { { "id[is]", $"user-{user.Id}" } }, data);
            // Pretend no existing customers found for this id.
            return new {
              list = new object[] {
                  new {
                    customer = new {
                      id = $"user-{user.Id}",
                    },
                  },
                },
              next_offset = null as string,
            };
          } else if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
            Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
            Assert.AreEqual("personal", data["plan_id[is]"]);

            return new {
              list = new object[] {
                  new {
                    subscription = new {
                      id = "existing-sub-id",
                      status = "active",
                      resource_version = 1234,
                    },
                  },
                },
              next_offset = null as string,
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        await CreateHandler(api).GetOrCreatePersonalSubscriptionHelper(new UserIdMessage(user.Id), collectorMock.Object, mockClient.Object, Console.Out);

        Assert.AreEqual(0, changeMessages.Count(),
          "should NOT send a notification because state did not change.");
      }
    }

    [Test]
    public async Task WillUpdateTrailEndDateFromExistingSubscription() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var subscription = context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.NotSubscribed,
        });
        await context.SaveChangesAsync();

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });
        var mockClient = new Mock<IGitHubActor>();

        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Login = "aroon",
              Name = "Aroon Pahwa",
              Type = Common.GitHub.Models.GitHubAccountType.User,
            }
          });

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path.Equals("/api/v2/customers")) {
            Assert.AreEqual(new Dictionary<string, string>() { { "id[is]", $"user-{user.Id}" } }, data);
            // Pretend no existing customers found for this id.
            return new {
              list = new object[] {
                  new {
                    customer = new {
                      id = $"user-{user.Id}",
                    },
                  },
                },
              next_offset = null as string,
            };
          } else if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
            Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
            Assert.AreEqual("personal", data["plan_id[is]"]);

            return new {
              list = new object[] {
                  new {
                    subscription = new {
                      id = "existing-sub-id",
                      status = "in_trial",
                      trial_end = DateTimeOffset.Parse(
                        "10/1/2016 08:00:00 PM +00:00",
                        null,
                        DateTimeStyles.AssumeUniversal).ToUnixTimeSeconds(),
                      resource_version = 1234,
                    },
                  },
                },
              next_offset = null as string,
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        await CreateHandler(api).GetOrCreatePersonalSubscriptionHelper(new UserIdMessage(user.Id), collectorMock.Object, mockClient.Object, Console.Out);

        context.Entry(subscription).Reload();
        Assert.AreEqual(SubscriptionState.InTrial, subscription.State,
          "should change to subscribed because chargebee says subscription is active.");
        Assert.AreEqual(DateTimeOffset.Parse(
                        "10/1/2016 08:00:00 PM +00:00",
                        null,
                        DateTimeStyles.AssumeUniversal),
                        subscription.TrialEndDate);
      }
    }

    [Test]
    public async Task WillNotCreateCustomerIfOneAlreadyExists() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Login = "aroon",
              Name = "Aroon Pahwa",
              Type = Common.GitHub.Models.GitHubAccountType.User,
            }
          });

        bool createdAccount = false;
        bool createdSubscription = false;

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path.Equals("/api/v2/customers")) {
            Assert.AreEqual(new Dictionary<string, string>() { { "id[is]", $"user-{user.Id}" } }, data);
            // Pretend no existing customers found for this id.
            return new {
              list = new object[] {
                  new {
                    customer = new {
                      id = $"user-{user.Id}",
                    },
                  },
                },
              next_offset = null as string,
            };
          } else if (method.Equals("POST") && path.Equals("/api/v2/customers")) {
            Assert.AreEqual(
              new Dictionary<string, string> {
                      { "id", $"user-{user.Id}" },
                      { "first_name", $"Aroon"},
                      { "last_name", $"Pahwa"},
              },
              data);
            createdAccount = true;
            // Fake response for customer creation.
            return new {
              customer = new {
                id = $"user-{user.Id}",
              },
            };
          } else if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
            Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
            Assert.AreEqual("personal", data["plan_id[is]"]);

            // Pretend no existing subscriptions found.
            return new {
              list = new object[0],
              next_offset = null as string,
            };
          } else if (method.Equals("POST") && path.Equals($"/api/v2/customers/user-{user.Id}/subscriptions")) {
            Assert.AreEqual(
              new Dictionary<string, string> {
                      { "plan_id", "personal"},
              },
              data);
            createdSubscription = true;
            // Fake response for creating the subscription.
            return new {
              subscription = new {
                id = "some-sub-id",
                status = "in_trial",
                trial_end = DateTimeOffset.Parse(
                      "10/1/2016 08:00:00 PM +00:00",
                      null,
                      DateTimeStyles.AssumeUniversal).ToUnixTimeSeconds(),
                resource_version = 1234,
              },
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        await CreateHandler(api).GetOrCreatePersonalSubscriptionHelper(new UserIdMessage(user.Id), collectorMock.Object, mockClient.Object, Console.Out);

        var sub = context.Subscriptions.Single(x => x.AccountId == user.Id);
        Assert.AreEqual(SubscriptionState.InTrial, sub.State);
        Assert.IsFalse(createdAccount);
        Assert.IsTrue(createdSubscription);
      }
    }

    [Test]
    public async Task WillNotCreateSubscriptionIfOneAlreadyExists() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = user.Id,
              Login = "aroon",
              Name = "Aroon Pahwa",
              Type = Common.GitHub.Models.GitHubAccountType.User,
            }
          });

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path.Equals("/api/v2/customers")) {
            Assert.AreEqual(new Dictionary<string, string>() { { "id[is]", $"user-{user.Id}" } }, data);
            // Pretend no existing customers found for this id.
            return new {
              list = new object[] {
                  new {
                    customer = new {
                      id = $"user-{user.Id}",
                    },
                  },
                },
              next_offset = null as string,
            };
          } else if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
            Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
            Assert.AreEqual("personal", data["plan_id[is]"]);

            // Pretend no existing subscriptions found.
            return new {
              list = new object[] {
                  new {
                    subscription = new {
                      id = "existing-sub-id",
                      status = "active",
                      resource_version = 1234,
                    },
                  },
                },
              next_offset = null as string,
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        await CreateHandler(api).GetOrCreatePersonalSubscriptionHelper(new UserIdMessage(user.Id), collectorMock.Object, mockClient.Object, Console.Out);

        var sub = context.Subscriptions.Single(x => x.AccountId == user.Id);
        Assert.AreEqual(SubscriptionState.Subscribed, sub.State);
      }
    }

    [Test]
    public async Task WillSyncOrgSubscriptionState() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        var mockClient = new Mock<IGitHubActor>();
        mockClient
          .Setup(x => x.User(It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<Common.GitHub.Models.Account>(null) {
            Result = new Common.GitHub.Models.Account() {
              Id = org.Id,
              Login = "aroon",
              Name = "Aroon Pahwa",
              Type = Common.GitHub.Models.GitHubAccountType.User,
            }
          });

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method.Equals("GET") && path.Equals("/api/v2/subscriptions")) {
            Assert.AreEqual($"org-{org.Id}", data["customer_id[is]"]);
            Assert.AreEqual("organization", data["plan_id[is]"]);

            return new {
              list = new object[] {
                  new {
                    subscription = new {
                      id = "existing-sub-id",
                      status = "active",
                      resource_version = 1234,
                    },
                  },
                },
              next_offset = null as string,
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        await CreateHandler(api).SyncOrgSubscriptionStateHelper(
          new TargetMessage() {
            TargetId = org.Id,
            ForUserId = user.Id,
          },
          collectorMock.Object, mockClient.Object, Console.Out);

        var subscription = context.Subscriptions.Single(x => x.AccountId == org.Id);
        Assert.AreEqual(SubscriptionState.Subscribed, subscription.State,
          "should show as subscribed");
        Assert.IsNull(subscription.TrialEndDate);
        Assert.AreEqual(1234, subscription.Version);

        Assert.AreEqual(
          new long[] { org.Id },
          changeMessages.FirstOrDefault()?.Organizations.OrderBy(x => x).ToArray(),
          "should notify that this org changed.");
      }
    }

    private static async Task UpdateComplimentarySubscriptionHelper(
      bool isMemberOfPaidOrg,
      bool memberHasSub,
      bool subHasCoupon,
      string subStatus,
      bool expectCouponAddition,
      bool expectCouponRemoval
      ) {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        await context.SetOrganizationUsers(org.Id, new[] {
          Tuple.Create(user.Id, true),
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.Subscribed,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = org.Id,
          State = isMemberOfPaidOrg ? SubscriptionState.Subscribed : SubscriptionState.NotSubscribed,
        });
        await context.SaveChangesAsync();

        bool didAddCoupon = false;
        bool didRemoveCoupon = false;

        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method == "GET" && path == "/api/v2/subscriptions") {
            Assert.AreEqual($"user-{user.Id}", data["customer_id[is]"]);
            Assert.AreEqual("personal", data["plan_id[is]"]);

            if (memberHasSub) {
              return new {
                list = new object[] {
                    new {
                      subscription = new {
                        id = "some-sub-id",
                        status = subStatus,
                        coupons = !subHasCoupon ?
                          new object[0] :
                          new[] {
                            new {
                              coupon_id = "member_of_paid_org",
                            },
                          },
                      },
                    },
                  },
                next_offset = null as string,
              };
            } else {
              return new {
                list = new object[0],
                next_offset = null as string,
              };
            }
          } else if (method == "POST" && path == "/api/v2/subscriptions/some-sub-id") {
            didAddCoupon = true;
            Assert.AreEqual("member_of_paid_org", data["coupon_ids[0]"]);

            return new {
              subscription = new {
                id = "some-sub-id",
              },
            };
          } else if (method == "POST" && path == "/api/v2/subscriptions/some-sub-id/remove_coupons") {
            didRemoveCoupon = true;
            Assert.AreEqual("member_of_paid_org", data["coupon_ids[0]"]);

            return new {
              subscription = new {
                id = "some-sub-id",
              },
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        var handler = new BillingQueueHandler(null, new DetailedExceptionLogger(), api);
        var executionContext = new Microsoft.Azure.WebJobs.ExecutionContext() {
          InvocationId = Guid.NewGuid()
        };
        await handler.UpdateComplimentarySubscription(new UserIdMessage(user.Id), Console.Out, executionContext);

        Assert.AreEqual(expectCouponAddition, didAddCoupon);
        Assert.AreEqual(expectCouponRemoval, didRemoveCoupon);
      }
    }

    [Test]
    public Task WillAddCouponWhenOrgIsPaidAndCouponIsMissing() {
      return UpdateComplimentarySubscriptionHelper(
        isMemberOfPaidOrg: true,
        memberHasSub: true,
        subStatus: "active",
        subHasCoupon: false,
        expectCouponAddition: true,
        expectCouponRemoval: false);
    }

    [Test]
    public Task WillRemoveCouponWhenOrgIsNotPaidAndCouponIsPresent() {
      return UpdateComplimentarySubscriptionHelper(
        isMemberOfPaidOrg: false,
        memberHasSub: true,
        subHasCoupon: true,
        subStatus: "active",
        expectCouponAddition: false,
        expectCouponRemoval: true);
    }

    [Test]
    public Task WillDoNothingWhenWhenOrgIsPaidAndCouponIsPresent() {
      return UpdateComplimentarySubscriptionHelper(
        isMemberOfPaidOrg: true,
        memberHasSub: true,
        subHasCoupon: true,
        subStatus: "active",
        expectCouponAddition: false,
        expectCouponRemoval: false);
    }

    [Test]
    public Task WillDoNothingWhenWhenOrgIsNotPaidAndCouponIsNotPresent() {
      return UpdateComplimentarySubscriptionHelper(
        isMemberOfPaidOrg: false,
        memberHasSub: true,
        subHasCoupon: false,
        subStatus: "active",
        expectCouponAddition: false,
        expectCouponRemoval: false);
    }

    [Test]
    public Task WillDoNothingWhenWhenOrgIsPaidButMemberHasNoSubscription() {
      return UpdateComplimentarySubscriptionHelper(
        isMemberOfPaidOrg: true,
        memberHasSub: false,
        subHasCoupon: false,
        subStatus: "active",
        expectCouponAddition: false,
        expectCouponRemoval: false);
    }

    [Test]
    public Task WillNotAddCouponWhenOrgIsPaidButMemberIsInTrial() {
      return UpdateComplimentarySubscriptionHelper(
        isMemberOfPaidOrg: true,
        memberHasSub: true,
        subHasCoupon: false,
        subStatus: "in_trial",
        expectCouponAddition: false,
        expectCouponRemoval: false);
    }
  }
}
