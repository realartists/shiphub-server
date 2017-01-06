﻿namespace RealArtists.ShipHub.Api.Tests {
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
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Controllers;
  using Mail;
  using Mail.Models;
  using Microsoft.Azure;
  using Moq;
  using Newtonsoft.Json;
  using NUnit.Framework;
  using QueueClient;

  [TestFixture]
  [AutoRollback]
  public class ChargeBeeWebhookControllerTests {
    public static void ConfigureController(ChargeBeeWebhookController controller, ChargeBeeWebhookPayload payload) {
      var json = JsonConvert.SerializeObject(payload, GitHubSerialization.JsonSerializerSettings);

      var config = new HttpConfiguration();
      var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/webhook") {
        Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json))
      };
      var routeData = new HttpRouteData(config.Routes.MapHttpRoute("Chargebee", "chargebee"));

      controller.ControllerContext = new HttpControllerContext(config, routeData, request);
      controller.Request = request;
      controller.Request.Properties[HttpPropertyKeys.HttpConfigurationKey] = config;
    }

    private static IShipHubConfiguration Configuration {
      get {
        var config = new Mock<IShipHubConfiguration>();
        config.Setup(x => x.ApiHostName).Returns("api.realartists.com");
        return config.Object;
      }
    }

    private static async Task TestSubscriptionStateChangeHelper(
      string userOrOrg,
      string eventType,
      string chargeBeeState,
      DateTimeOffset? chargeBeeTrialEndDate,
      SubscriptionState beginState,
      DateTimeOffset? beginTrialEndDate,
      SubscriptionState expectedState,
      DateTimeOffset? expectedTrialEndDate,
      bool notifyExpected,
      ChargeBeeWebhookInvoice invoicePayload = null,
      List<MailMessageBase> outgoingMessages = null,
      bool userBelongsToOrganization = false
      ) {
      using (var context = new ShipHubContext()) {
        User user = null;
        Organization org = null;
        Subscription sub = null;

        if (userOrOrg == "user") {
          user = TestUtil.MakeTestUser(context);
          sub = context.Subscriptions.Add(new Subscription() {
            AccountId = user.Id,
            State = beginState,
            TrialEndDate = beginTrialEndDate,
            Version = 0,
          });

          if (userBelongsToOrganization) {
            org = TestUtil.MakeTestOrg(context);
            await context.SetOrganizationUsers(org.Id, new[] {
              Tuple.Create(user.Id, true),
            });
          }
        } else {
          org = TestUtil.MakeTestOrg(context);
          sub = context.Subscriptions.Add(new Subscription() {
            AccountId = org.Id,
            State = beginState,
            TrialEndDate = beginTrialEndDate,
            Version = 0,
          });
        }

        await context.SaveChangesAsync();

        IChangeSummary changeSummary = null;
        var mockBusClient = new Mock<IShipHubQueueClient>();
        mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
          .Returns(Task.CompletedTask)
          .Callback((IChangeSummary arg) => { changeSummary = arg; });

        var mockMailer = new Mock<IShipHubMailer>();
        mockMailer
          .Setup(x => x.PurchasePersonal(It.IsAny<PurchasePersonalMailMessage>()))
          .Returns(Task.CompletedTask)
          .Callback((PurchasePersonalMailMessage message) => outgoingMessages?.Add(message));
        mockMailer
          .Setup(x => x.PurchaseOrganization(It.IsAny<PurchaseOrganizationMailMessage>()))
          .Returns(Task.CompletedTask)
          .Callback((PurchaseOrganizationMailMessage message) => outgoingMessages?.Add(message));

        var controller = new Mock<ChargeBeeWebhookController>(Configuration, mockBusClient.Object, mockMailer.Object, null) {
          CallBase = true
        };

        ConfigureController(
          controller.Object,
          new ChargeBeeWebhookPayload() {
            EventType = eventType,
            Content = new ChargeBeeWebhookContent() {
              Customer = new ChargeBeeWebhookCustomer() {
                Id = $"{userOrOrg}-{sub.AccountId}",
                FirstName = "Aroon",
                LastName = "Pahwa",
                GitHubUserName = "aroon",
              },
              Subscription = new ChargeBeeWebhookSubscription() {
                Status = chargeBeeState,
                TrialEnd = chargeBeeTrialEndDate?.ToUnixTimeSeconds(),
                PlanId = (userOrOrg == "user") ? "personal" : "organization",
              },
              Invoice = invoicePayload,
            },
          });
        await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);

        context.Entry(sub).Reload();
        Assert.AreEqual(expectedState, sub.State);
        Assert.AreEqual(expectedTrialEndDate, sub.TrialEndDate);

        if (notifyExpected) {
          if (userOrOrg == "org") {
            Assert.AreEqual(new long[] { org.Id }, changeSummary?.Organizations.ToArray());
            Assert.AreEqual(new long[] { }, changeSummary?.Users.ToArray());
          } else {
            Assert.AreEqual(new long[] { user.Id }, changeSummary?.Users.ToArray());
            Assert.AreEqual(new long[] { }, changeSummary?.Organizations.ToArray());
          }
        } else {
          Assert.IsNull(changeSummary);
        }
      };
    }

    [Test]
    public async Task ShouldNotNotifyIfNothingChanged() {
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "user",
        eventType: "subscription_changed",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.Subscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: false);
    }

    [Test]
    public async Task SubscriptionActivated() {
      await TestSubscriptionStateChangeHelper(
        // Triggered after the subscription has been moved from "Trial" to "Active" state 
        userOrOrg: "user",
        eventType: "subscription_activated",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.InTrial,
        beginTrialEndDate: DateTimeOffset.Parse("2020-09-22T00:00:00+00:00"),
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: true,
        invoicePayload: new ChargeBeeWebhookInvoice() {
          Id = "inv_1234",
          Date = new DateTimeOffset(2016, 11, 08, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        });
    }

    [Test]
    public async Task SubscriptionActivatedSendsPurchasePersonalMessage() {
      var messages = new List<MailMessageBase>();
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "user",
        eventType: "subscription_activated",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.InTrial,
        beginTrialEndDate: DateTimeOffset.Parse("2016-11-30T00:00:00+00:00"),
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: true,
        invoicePayload: new ChargeBeeWebhookInvoice() {
          Id = "inv_1234",
          Date = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
          Discounts = new[] {
                  new ChargeBeeWebhookInvoiceDiscount() {
                    EntityType = "document_level_coupon",
                    EntityId = "trial_days_left_15",
                  },
                },
        },
        outgoingMessages: messages,
        userBelongsToOrganization: true);

      Assert.AreEqual(1, messages.Count);
      var message = (PurchasePersonalMailMessage)messages.First();
      Assert.AreEqual("Aroon", message.FirstName);
      Assert.AreEqual("aroon", message.GitHubUserName);
      Assert.AreEqual(true, message.BelongsToOrganization);
      Assert.AreEqual(true, message.WasGivenTrialCredit);
    }

    [Test]
    public async Task SubscriptionChanged() {
      await TestSubscriptionStateChangeHelper(
        // ChargeBee says "Triggered when the subscription's recurring items are changed",
        // but in my experience this comes when a user transitions from trial to active via
        // the checkout page.
        userOrOrg: "user",
        eventType: "subscription_changed",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.InTrial,
        beginTrialEndDate: DateTimeOffset.Parse("2020-09-22T00:00:00+00:00"),
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: true);
    }

    [Test]
    public async Task SubscriptionReactivatedToActive() {
      // Triggered when the subscription is moved from cancelled state to "Active" or "Trial" state
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "user",
        eventType: "subscription_reactivated",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.NotSubscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: true,
        invoicePayload: new ChargeBeeWebhookInvoice() {
          Id = "inv_1234",
          Date = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        });
    }

    [Test]
    public async Task SubscriptionReactivatedToTrial() {
      // Triggered when the subscription is moved from cancelled state to "Active" or "Trial" state
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "user",
        eventType: "subscription_reactivated",
        chargeBeeState: "in_trial",
        chargeBeeTrialEndDate: DateTimeOffset.Parse("2020-09-22T00:00:00+00:00"),
        beginState: SubscriptionState.NotSubscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.InTrial,
        expectedTrialEndDate: DateTimeOffset.Parse("2020-09-22T00:00:00+00:00"),
        notifyExpected: true,
        invoicePayload: new ChargeBeeWebhookInvoice() {
          Id = "inv_1234",
          Date = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        });
    }

    [Test]
    public async Task SubscriptionStarted() {
      // Triggered when a 'future' subscription gets started
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "user",
        eventType: "subscription_started",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.NotSubscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: true);
    }

    [Test]
    public async Task SubscriptionCancelled() {
      // Triggered when the subscription is cancelled. If it is cancelled due
      // to non payment or because the card details are not present, the
      // subscription will have the possible reason as 'cancel_reason'. 
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "user",
        eventType: "subscription_cancelled",
        chargeBeeState: "cancelled",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.Subscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.NotSubscribed,
        expectedTrialEndDate: null,
        notifyExpected: true);
    }

    [Test]
    public async Task SubscriptionDeleted() {
      // Triggered when the subscription is cancelled. If it is cancelled due
      // to non payment or because the card details are not present, the
      // subscription will have the possible reason as 'cancel_reason'. 
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "user",
        eventType: "subscription_deleted",
        chargeBeeState: "cancelled",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.Subscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.NotSubscribed,
        expectedTrialEndDate: null,
        notifyExpected: true);
    }

    [Test]
    public async Task SubscriptionCreated() {
      // Triggered when a subscription is created and is active from the start.
      // e.g., when someone purchases an org subscription which has no trial.
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "org",
        eventType: "subscription_created",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.NotSubscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: true,
        invoicePayload: new ChargeBeeWebhookInvoice() {
          Id = "inv_1234",
          Date = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        });
    }

    [Test]
    public async Task SubscriptionCreatedSendsPurchaseOrganizationMessage() {
      var messages = new List<MailMessageBase>();
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "org",
        eventType: "subscription_created",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.NotSubscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: true,
        invoicePayload: new ChargeBeeWebhookInvoice() {
          Id = "inv_1234",
          Date = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        },
        outgoingMessages: messages);

      Assert.AreEqual(1, messages.Count);
      var message = (PurchaseOrganizationMailMessage)messages.First();
      Assert.AreEqual("Aroon", message.FirstName);
      Assert.AreEqual("aroon", message.GitHubUserName);
    }

    [Test]
    public async Task CustomerDeleted() {
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "user",
        eventType: "customer_deleted",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.Subscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.NotSubscribed,
        expectedTrialEndDate: null,
        notifyExpected: true);
    }

    [Test]
    public async Task SubscriptionCreatedForOrgShouldNotifyOrg() {
      // Triggered when a subscription is created and is active from the start.
      // e.g., when someone purchases an org subscription which has no trial.
      await TestSubscriptionStateChangeHelper(
        userOrOrg: "org",
        eventType: "subscription_created",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.NotSubscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: true,
        invoicePayload: new ChargeBeeWebhookInvoice() {
          Id = "inv_1234",
          Date = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        });
    }

    [Test]
    public async Task ShouldCloseInvoicesForPersonalSubscriptions() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var sub = context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.Subscribed,
          Version = 0,
        });
        await context.SaveChangesAsync();

        bool didCloseInvoice = false;
        var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
          if (method == "POST" && path == "/api/v2/invoices/draft_inv_123/close") {
            didCloseInvoice = true;
            return new {
              invoice = new { },
            };
          } else {
            Assert.Fail($"Unexpected {method} to {path}");
            return null;
          }
        });

        var mockBusClient = new Mock<IShipHubQueueClient>();
        var mockMailer = new Mock<IShipHubMailer>();
        var controller = new ChargeBeeWebhookController(Configuration, mockBusClient.Object, mockMailer.Object, api);
        ConfigureController(
          controller,
          new ChargeBeeWebhookPayload() {
            EventType = "pending_invoice_created",
            Content = new ChargeBeeWebhookContent() {
              Customer = new ChargeBeeWebhookCustomer() {
                GitHubUserName = "pureimaginary",
              },
              Invoice = new ChargeBeeWebhookInvoice() {
                Id = "draft_inv_123",
                CustomerId = $"user-{user.Id}",
                LineItems = new[] {
                  new ChargeBeeWebhookInvoiceLineItem() {
                    EntityType = "plan",
                    EntityId = "personal",
                    DateFrom = new DateTimeOffset(2016, 2, 1, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                  },
                },
              }
            },
          });

        await controller.HandleHook(Configuration.ChargeBeeWebhookSecret);

        Assert.IsTrue(didCloseInvoice);
      };
    }

    private static async Task<List<Tuple<string, int>>> PendingInvoiceCreatedHelper(long orgId, DateTimeOffset dateFrom) {
      var payload = new ChargeBeeWebhookPayload() {
        EventType = "pending_invoice_created",
        Content = new ChargeBeeWebhookContent() {
          Customer = new ChargeBeeWebhookCustomer() {
            GitHubUserName = "pureimaginary",
          },
          Invoice = new ChargeBeeWebhookInvoice() {
            Id = "draft_inv_123",
            CustomerId = $"org-{orgId}",
            LineItems = new[] {
                  new ChargeBeeWebhookInvoiceLineItem() {
                    EntityType = "plan",
                    EntityId = "organization",
                    DateFrom = dateFrom.ToUnixTimeSeconds(),
                  },
                },
          }
        }
      };

      bool didCloseInvoice = false;
      var addons = new List<Tuple<string, int>>();

      var api = ChargeBeeTestUtil.ShimChargeBeeApi((string method, string path, Dictionary<string, string> data) => {
        if (method == "POST" && path == "/api/v2/invoices/draft_inv_123/close") {
          didCloseInvoice = true;

          // don't care about return values.
          return new {
            invoice = new { },
          };
        } else if (method == "POST" && path == $"/api/v2/invoices/{payload.Content.Invoice.Id}/add_addon_charge") {
          addons.Add(Tuple.Create(data["addon_id"], int.Parse(data["addon_quantity"])));

          // don't care about return values.
          return new {
            invoice = new { },
          };
        } else {
          Assert.Fail($"Unexpected {method} to {path}");
          return null;
        }
      });

      var mockBusClient = new Mock<IShipHubQueueClient>();
      var mockMailer = new Mock<IShipHubMailer>();
      var controller = new ChargeBeeWebhookController(Configuration, mockBusClient.Object, mockMailer.Object, api);
      ConfigureController(controller, payload);

      await controller.HandleHook(Configuration.ChargeBeeWebhookSecret);

      Assert.IsTrue(didCloseInvoice);

      return addons;
    }

    [Test]
    public async Task PendingInvoiceCreatedAddsActiveUsersCharge() {
      using (var context = new ShipHubContext()) {

        var invoiceDateFrom = new DateTimeOffset(2016, 2, 1, 12, 0, 0, TimeSpan.Zero);

        var org1 = TestUtil.MakeTestOrg(context);

        var users = new List<User>();
        for (int i = 0; i < 20; i++) {
          var user = TestUtil.MakeTestUser(context, 3001 + i, "aroo" + "".PadLeft(i, 'o') + "n");
          users.Add(user);
        }

        var otherOrg = TestUtil.MakeTestOrg(context, 6002, "otherOrrg");
        var otherOrgUser = TestUtil.MakeTestUser(context, 4001, "otherOrgUser");
        await context.SetOrganizationUsers(otherOrg.Id,
          new[] { new Tuple<long, bool>(otherOrgUser.Id, true) });

        // Pretend all 20 people use Ship in January
        foreach (var user in users) {
          await context.RecordUsage(user.Id, new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero));
        }
        // Add some usage in another org - this should get filtered out when we calculate
        // active users.
        await context.RecordUsage(otherOrgUser.Id, new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero));


        // Only 5 people use it in February
        foreach (var user in users.Take(5)) {
          await context.RecordUsage(user.Id, new DateTimeOffset(2016, 2, 1, 0, 0, 0, TimeSpan.Zero));
        }

        // Only 5 people use it in February
        foreach (var user in users.Take(5)) {
          await context.RecordUsage(user.Id, new DateTimeOffset(2016, 3, 1, 0, 0, 0, TimeSpan.Zero));
        }

        // Pretend all 20 people use Ship on March 1
        foreach (var user in users) {
          await context.RecordUsage(user.Id, new DateTimeOffset(2016, 3, 1, 0, 0, 0, TimeSpan.Zero));
        }

        // Pretend that for the billing period from March 2 - April 1 [inclusive], 8 people use the
        // product at various times.
        await context.RecordUsage(users[0].Id, new DateTimeOffset(2016, 3, 2, 0, 0, 0, TimeSpan.Zero));
        await context.RecordUsage(users[0].Id, new DateTimeOffset(2016, 3, 3, 0, 0, 0, TimeSpan.Zero));
        await context.RecordUsage(users[1].Id, new DateTimeOffset(2016, 3, 5, 0, 0, 0, TimeSpan.Zero));
        await context.RecordUsage(users[2].Id, new DateTimeOffset(2016, 3, 9, 0, 0, 0, TimeSpan.Zero));
        await context.RecordUsage(users[3].Id, new DateTimeOffset(2016, 3, 15, 0, 0, 0, TimeSpan.Zero));
        await context.RecordUsage(users[4].Id, new DateTimeOffset(2016, 3, 20, 0, 0, 0, TimeSpan.Zero));
        await context.RecordUsage(users[5].Id, new DateTimeOffset(2016, 3, 26, 0, 0, 0, TimeSpan.Zero));
        await context.RecordUsage(users[6].Id, new DateTimeOffset(2016, 3, 29, 0, 0, 0, TimeSpan.Zero));
        await context.RecordUsage(users[7].Id, new DateTimeOffset(2016, 4, 1, 0, 0, 0, TimeSpan.Zero));

        // Pretend all 20 people use Ship on April 2
        foreach (var user in users) {
          await context.RecordUsage(user.Id, new DateTimeOffset(2016, 4, 2, 0, 0, 0, TimeSpan.Zero));
        }

        await context.SetOrganizationUsers(org1.Id,
          users.Select(x => new Tuple<long, bool>(x.Id, true)));

        await context.SaveChangesAsync();

        Assert.AreEqual(
          new[] { Tuple.Create("additional-seats", 15) },
          await PendingInvoiceCreatedHelper(org1.Id, new DateTimeOffset(2016, 2, 1, 0, 0, 0, TimeSpan.Zero)),
          "For billing period [1/1 - 1/31], we should get billed for 15 extra seats.");

        Assert.AreEqual(
          new Tuple<string, int>[0],
          await PendingInvoiceCreatedHelper(org1.Id, new DateTimeOffset(2016, 3, 1, 0, 0, 0, TimeSpan.Zero)),
          "For billing period [2/1 - 2/31], we should not see any extra charge - only 5 people were active.");

        Assert.AreEqual(
          new[] { Tuple.Create("additional-seats", 3) },
          await PendingInvoiceCreatedHelper(org1.Id, new DateTimeOffset(2016, 4, 2, 0, 0, 0, TimeSpan.Zero)),
          "For billing period [3/2 - 4/1], there were only 8 active users.");
      };
    }

    [Test]
    public async Task ShouldIgnoreEventsWithOldResourceVersion() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var sub = context.Subscriptions.Add(new Subscription() {
          AccountId = user.Id,
          State = SubscriptionState.Subscribed,
          TrialEndDate = null,
          Version = 0,
        });

        await context.SaveChangesAsync();

        Func<string, long, ChargeBeeWebhookPayload> makeSubscriptionChangedEvent = (string status, long resourceVersion) =>
          new ChargeBeeWebhookPayload() {
            EventType = "subscription_changed",
            Content = new ChargeBeeWebhookContent() {
              Customer = new ChargeBeeWebhookCustomer() {
                Id = $"user-{sub.AccountId}",
              },
              Subscription = new ChargeBeeWebhookSubscription() {
                Status = status,
                ResourceVersion = resourceVersion,
              },
            },
          };

        Func<string, long, ChargeBeeWebhookPayload> makeSubscriptionReactivatedEvent = (string status, long resourceVersion) =>
          new ChargeBeeWebhookPayload() {
            EventType = "subscription_reactivated",
            Content = new ChargeBeeWebhookContent() {
              Customer = new ChargeBeeWebhookCustomer() {
                Id = $"user-{sub.AccountId}",
              },
              Subscription = new ChargeBeeWebhookSubscription() {
                Status = status,
                ResourceVersion = resourceVersion,
              },
            },
          };

        Func<long, ChargeBeeWebhookPayload> makeCustomerDeletedEvent = (long resourceVersion) =>
          new ChargeBeeWebhookPayload() {
            EventType = "customer_deleted",
            Content = new ChargeBeeWebhookContent() {
              Customer = new ChargeBeeWebhookCustomer() {
                Id = $"user-{sub.AccountId}",
                ResourceVersion = resourceVersion,
              },
            },
          };

        Func<ChargeBeeWebhookPayload, Task> fireEvent = (ChargeBeeWebhookPayload payload) => {
          var mockBusClient = new Mock<IShipHubQueueClient>();
          var mockMailer = new Mock<IShipHubMailer>();
          var controller = new ChargeBeeWebhookController(Configuration, mockBusClient.Object, mockMailer.Object, null);
          ConfigureController(controller, payload);
          return controller.HandleHook(Configuration.ChargeBeeWebhookSecret);
        };

        // should see version advance.
        await fireEvent(makeSubscriptionChangedEvent("active", 1000));
        context.Entry(sub).Reload();
        Assert.AreEqual(1000, sub.Version);
        Assert.AreEqual(SubscriptionState.Subscribed, sub.State);

        // should accept because version advances.
        await fireEvent(makeSubscriptionChangedEvent("cancelled", 1001));
        context.Entry(sub).Reload();
        Assert.AreEqual(1001, sub.Version);
        Assert.AreEqual(SubscriptionState.NotSubscribed, sub.State);

        // should ignore since version is older.
        await fireEvent(makeSubscriptionChangedEvent("active", 900));
        context.Entry(sub).Reload();
        Assert.AreEqual(1001, sub.Version);
        Assert.AreEqual(SubscriptionState.NotSubscribed, sub.State);

        // should accept since version advances.
        await fireEvent(makeSubscriptionChangedEvent("active", 2000));
        context.Entry(sub).Reload();
        Assert.AreEqual(2000, sub.Version);
        Assert.AreEqual(SubscriptionState.Subscribed, sub.State);

        // should ignore customer deleted since version is older
        await fireEvent(makeCustomerDeletedEvent(1999));
        context.Entry(sub).Reload();
        Assert.AreEqual(2000, sub.Version);
        Assert.AreEqual(SubscriptionState.Subscribed, sub.State);

        // should accept deletion since version advanced.
        await fireEvent(makeCustomerDeletedEvent(2001));
        context.Entry(sub).Reload();
        Assert.AreEqual(2001, sub.Version);
        Assert.AreEqual(SubscriptionState.NotSubscribed, sub.State);

        // should not accept because resource_version is ignored for
        // "subscription_reactivated" events.
        await fireEvent(makeSubscriptionReactivatedEvent("active", 3000));
        context.Entry(sub).Reload();
        Assert.AreEqual(3000, sub.Version);
        Assert.AreEqual(SubscriptionState.Subscribed, sub.State);
      };
    }

    [Test]
    public async Task WillScheduleUpdateComplimentarySubscriptionOnStateChange() {
      using (var context = new ShipHubContext()) {
        var user1 = TestUtil.MakeTestUser(context, 3001, "aroon");
        var user2 = TestUtil.MakeTestUser(context, 3002, "alok");
        var org = TestUtil.MakeTestOrg(context);

        context.Subscriptions.Add(new Subscription() {
          AccountId = org.Id,
          State = SubscriptionState.Subscribed,
          Version = 0,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = user1.Id,
          State = SubscriptionState.Subscribed,
          Version = 0,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = user2.Id,
          State = SubscriptionState.Subscribed,
          Version = 0,
        });

        await context.SetOrganizationUsers(org.Id, new[] {
          Tuple.Create(user1.Id, true),
          Tuple.Create(user2.Id, true),
        });

        await context.SaveChangesAsync();

        var scheduledUserIds = new List<long>();
        var mockBusClient = new Mock<IShipHubQueueClient>();
        mockBusClient
          .Setup(x => x.BillingUpdateComplimentarySubscription(It.IsAny<long>()))
          .Returns(Task.CompletedTask)
          .Callback((long userId) => { scheduledUserIds.Add(userId); });
        var mockMailer = new Mock<IShipHubMailer>();
        var controller = new ChargeBeeWebhookController(Configuration, mockBusClient.Object, mockMailer.Object, null);
        ConfigureController(
          controller,
          new ChargeBeeWebhookPayload() {
            EventType = "subscription_cancelled",
            Content = new ChargeBeeWebhookContent() {
              Customer = new ChargeBeeWebhookCustomer() {
                Id = $"org-{org.Id}",
              },
              Subscription = new ChargeBeeWebhookSubscription() {
                Status = "cancelled",
                ResourceVersion = 1234,
              }
            },
          });
        await controller.HandleHook(Configuration.ChargeBeeWebhookSecret);

        Assert.AreEqual(new[] { 3001, 3002 }, scheduledUserIds.ToArray());
      };
    }

    [Test]
    public async Task PaymentSucceededForOrganizationSendsMessage() {
      var invoiceDate = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero);

      using (var context = new ShipHubContext()) {
        var org = TestUtil.MakeTestOrg(context);

        var users = new List<User>();
        for (int i = 0; i < 7; i++) {
          var user = TestUtil.MakeTestUser(context, 3001 + i, "aroo" + "".PadLeft(i, 'o') + "n");
          users.Add(user);
        }
        await context.SaveChangesAsync();

        await context.SetOrganizationUsers(org.Id, users.Select(x => Tuple.Create(x.Id, false)));
        foreach (var user in users) {
          await context.RecordUsage(user.Id, invoiceDate.AddDays(-15));
        }

        var mockBusClient = new Mock<IShipHubQueueClient>();
        mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
          .Returns(Task.CompletedTask);

        var outgoingMessages = new List<MailMessageBase>();
        var mockMailer = new Mock<IShipHubMailer>();
        mockMailer
          .Setup(x => x.PaymentSucceededOrganization(It.IsAny<PaymentSucceededOrganizationMailMessage>()))
          .Returns(Task.CompletedTask)
          .Callback((PaymentSucceededOrganizationMailMessage message) => outgoingMessages?.Add(message));

        var controller = new Mock<ChargeBeeWebhookController>(Configuration, mockBusClient.Object, mockMailer.Object, null) {
          CallBase = true
        };

        ConfigureController(
          controller.Object,
          new ChargeBeeWebhookPayload() {
            EventType = "payment_succeeded",
            Content = new ChargeBeeWebhookContent() {
              Customer = new ChargeBeeWebhookCustomer() {
                Id = $"org-{org.Id}",
                Email = "aroon@pureimaginary.com",
                FirstName = "Aroon",
                LastName = "Pahwa",
                GitHubUserName = org.Login,
              },
              Subscription = new ChargeBeeWebhookSubscription() {
                Status = "active",
                PlanId = "organization",
              },
              Invoice = new ChargeBeeWebhookInvoice() {
                Id = "inv_1234",
                Date = invoiceDate.ToUnixTimeSeconds(),
                LineItems = new[] {
                  new ChargeBeeWebhookInvoiceLineItem() {
                    EntityType = "plan",
                    EntityId = "organization",
                    DateFrom = invoiceDate.ToUnixTimeSeconds(),
                    DateTo = invoiceDate.AddMonths(1).ToUnixTimeSeconds(),
                  },
                },
                AmountPaid = 2500 + (900 * 2),
                FirstInvoice = false,
              },
              Transaction = new ChargeBeeWebhookTransaction() {
                MaskedCardNumber = "************4567",
              },
            },
          });
        await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);

        Assert.AreEqual(1, outgoingMessages.Count);
        var outgoingMessage = (PaymentSucceededOrganizationMailMessage)outgoingMessages.First();
        Assert.AreEqual("aroon@pureimaginary.com", outgoingMessage.ToAddress);
        Assert.AreEqual("Aroon Pahwa", outgoingMessage.ToName);
        Assert.AreEqual("Aroon", outgoingMessage.FirstName);
        Assert.AreEqual(43.00, outgoingMessage.AmountPaid);
        Assert.AreEqual("4567", outgoingMessage.LastCardDigits);
        Assert.AreEqual("/billing/invoice/inv_1234/b37d7cf6/ship-invoice-myorg-2016-11-15.pdf", new Uri(outgoingMessage.InvoicePdfUrl).AbsolutePath);
        Assert.AreEqual(invoiceDate.AddMonths(1), outgoingMessage.ServiceThroughDate);
        Assert.AreEqual(invoiceDate.AddMonths(-1), outgoingMessage.PreviousMonthStart);
        Assert.AreEqual(7, outgoingMessage.PreviousMonthActiveUsersCount);
        Assert.AreEqual(new[] {
          "aroon",
          "arooon",
          "aroooon",
          "arooooon",
          "aroooooon",
          "arooooooon",
          "aroooooooon",
        }, outgoingMessage.PreviousMonthActiveUsersSample);
      }
    }

    [Test]
    public async Task PaymentSucceededForPersonalSendsMessage() {
      var mockBusClient = new Mock<IShipHubQueueClient>();
      mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns(Task.CompletedTask);

      var outgoingMessages = new List<MailMessageBase>();
      var mockMailer = new Mock<IShipHubMailer>();
      mockMailer
        .Setup(x => x.PaymentSucceededPersonal(It.IsAny<PaymentSucceededPersonalMailMessage>()))
        .Returns(Task.CompletedTask)
        .Callback((PaymentSucceededPersonalMailMessage message) => outgoingMessages?.Add(message));

      var controller = new Mock<ChargeBeeWebhookController>(Configuration, mockBusClient.Object, mockMailer.Object, null) {
        CallBase = true
      };

      var invoiceDate = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero);

      ConfigureController(
        controller.Object,
        new ChargeBeeWebhookPayload() {
          EventType = "payment_succeeded",
          Content = new ChargeBeeWebhookContent() {
            Customer = new ChargeBeeWebhookCustomer() {
              Id = $"user-1234",
              Email = "aroon@pureimaginary.com",
              FirstName = "Aroon",
              LastName = "Pahwa",
              GitHubUserName = "aroon",
            },
            Subscription = new ChargeBeeWebhookSubscription() {
              Status = "active",
              PlanId = "personal",
            },
            Invoice = new ChargeBeeWebhookInvoice() {
              Id = "inv_1234",
              Date = invoiceDate.ToUnixTimeSeconds(),
              LineItems = new[] {
                new ChargeBeeWebhookInvoiceLineItem() {
                  EntityType = "plan",
                  EntityId = "personal",
                  DateFrom = invoiceDate.ToUnixTimeSeconds(),
                  DateTo = invoiceDate.AddMonths(1).ToUnixTimeSeconds(),
                },
              },
              AmountPaid = 900,
              FirstInvoice = false,
            },
            Transaction = new ChargeBeeWebhookTransaction() {
              MaskedCardNumber = "************4567",
            },
          },
        });
      await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);

      Assert.AreEqual(1, outgoingMessages.Count);
      var outgoingMessage = (PaymentSucceededPersonalMailMessage)outgoingMessages.First();
      Assert.AreEqual("aroon@pureimaginary.com", outgoingMessage.ToAddress);
      Assert.AreEqual("Aroon Pahwa", outgoingMessage.ToName);
      Assert.AreEqual("Aroon", outgoingMessage.FirstName);
      Assert.AreEqual(9.00, outgoingMessage.AmountPaid);
      Assert.AreEqual("4567", outgoingMessage.LastCardDigits);
      Assert.AreEqual("/billing/invoice/inv_1234/b37d7cf6/ship-invoice-aroon-2016-11-15.pdf", new Uri(outgoingMessage.InvoicePdfUrl).AbsolutePath);
      Assert.AreEqual(invoiceDate.AddMonths(1), outgoingMessage.ServiceThroughDate);
    }

    [Test]
    public async Task PaymentRefundedSendsMessage() {
      var mockBusClient = new Mock<IShipHubQueueClient>();
      mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns(Task.CompletedTask);

      var outgoingMessages = new List<MailMessageBase>();
      var mockMailer = new Mock<IShipHubMailer>();
      mockMailer
        .Setup(x => x.PaymentRefunded(It.IsAny<PaymentRefundedMailMessage>()))
        .Returns(Task.CompletedTask)
        .Callback((PaymentRefundedMailMessage message) => outgoingMessages?.Add(message));

      var controller = new Mock<ChargeBeeWebhookController>(Configuration, mockBusClient.Object, mockMailer.Object, null) {
        CallBase = true
      };

      var creditNotDate = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero);

      ConfigureController(
        controller.Object,
        new ChargeBeeWebhookPayload() {
          EventType = "payment_refunded",
          Content = new ChargeBeeWebhookContent() {
            Customer = new ChargeBeeWebhookCustomer() {
              Id = $"user-1234",
              Email = "aroon@pureimaginary.com",
              FirstName = "Aroon",
              LastName = "Pahwa",
              GitHubUserName = "aroon",
            },
            Subscription = new ChargeBeeWebhookSubscription() {
              Status = "active",
              PlanId = "personal",
            },
            CreditNote = new ChargeBeeWebhookCreditNote() {
              Id = "cn-1234",
              Date = creditNotDate.ToUnixTimeSeconds(),
              AmountRefunded = 900,
            },
            Transaction = new ChargeBeeWebhookTransaction() {
              MaskedCardNumber = "************4567",
            },
          },
        });
      await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);

      Assert.AreEqual(1, outgoingMessages.Count);
      var outgoingMessage = (PaymentRefundedMailMessage)outgoingMessages.First();
      Assert.AreEqual("aroon@pureimaginary.com", outgoingMessage.ToAddress);
      Assert.AreEqual("Aroon Pahwa", outgoingMessage.ToName);
      Assert.AreEqual("Aroon", outgoingMessage.FirstName);
      Assert.AreEqual(9.00, outgoingMessage.AmountRefunded);
      Assert.AreEqual("4567", outgoingMessage.LastCardDigits);
      Assert.AreEqual("aroon", outgoingMessage.GitHubUserName);
    }


    [Test]
    public async Task PaymentFailedSendsMessage() {
      var mockBusClient = new Mock<IShipHubQueueClient>();
      mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns(Task.CompletedTask);

      var outgoingMessages = new List<MailMessageBase>();
      var mockMailer = new Mock<IShipHubMailer>();
      mockMailer
        .Setup(x => x.PaymentFailed(It.IsAny<PaymentFailedMailMessage>()))
        .Returns(Task.CompletedTask)
        .Callback((PaymentFailedMailMessage message) => outgoingMessages?.Add(message));

      var controller = new Mock<ChargeBeeWebhookController>(Configuration, mockBusClient.Object, mockMailer.Object, null) {
        CallBase = true
      };

      var invoiceDate = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero);

      ConfigureController(
        controller.Object,
        new ChargeBeeWebhookPayload() {
          EventType = "payment_failed",
          Content = new ChargeBeeWebhookContent() {
            Customer = new ChargeBeeWebhookCustomer() {
              Id = $"user-1234",
              Email = "aroon@pureimaginary.com",
              FirstName = "Aroon",
              LastName = "Pahwa",
              GitHubUserName = "aroon",
            },
            Subscription = new ChargeBeeWebhookSubscription() {
              Status = "active",
              PlanId = "personal",
            },
            Invoice = new ChargeBeeWebhookInvoice() {
              Id = "inv-1234",
              Date = invoiceDate.ToUnixTimeSeconds(),
              NextRetryAt = invoiceDate.AddDays(3).ToUnixTimeSeconds(),
            },
            Transaction = new ChargeBeeWebhookTransaction() {
              Amount = 900,
              MaskedCardNumber = "************4567",
            },
          },
        });
      await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);

      Assert.AreEqual(1, outgoingMessages.Count);
      var outgoingMessage = (PaymentFailedMailMessage)outgoingMessages.First();
      Assert.AreEqual("aroon@pureimaginary.com", outgoingMessage.ToAddress);
      Assert.AreEqual("Aroon Pahwa", outgoingMessage.ToName);
      Assert.AreEqual("aroon", outgoingMessage.GitHubUserName);
      Assert.AreEqual(9.00, outgoingMessage.Amount);
      Assert.AreEqual("4567", outgoingMessage.LastCardDigits);
      Assert.AreEqual("/billing/invoice/inv-1234/50684b9b/ship-invoice-aroon-2016-11-15.pdf", new Uri(outgoingMessage.InvoicePdfUrl).AbsolutePath);
      Assert.NotNull(outgoingMessage.UpdatePaymentMethodUrl);
      Assert.AreEqual(invoiceDate.AddDays(3), outgoingMessage.NextRetryDate);
    }

    [Test]
    public async Task CardExpiredSendsMessage() {
      var mockBusClient = new Mock<IShipHubQueueClient>();
      mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns(Task.CompletedTask);

      var outgoingMessages = new List<MailMessageBase>();
      var mockMailer = new Mock<IShipHubMailer>();
      mockMailer
        .Setup(x => x.CardExpiryReminder(It.IsAny<CardExpiryReminderMailMessage>()))
        .Returns(Task.CompletedTask)
        .Callback((CardExpiryReminderMailMessage message) => outgoingMessages?.Add(message));

      var controller = new Mock<ChargeBeeWebhookController>(Configuration, mockBusClient.Object, mockMailer.Object, null) {
        CallBase = true
      };

      var termEndDate = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero);

      ConfigureController(
        controller.Object,
        new ChargeBeeWebhookPayload() {
          EventType = "card_expired",
          Content = new ChargeBeeWebhookContent() {
            Customer = new ChargeBeeWebhookCustomer() {
              Id = $"user-1234",
              Email = "aroon@pureimaginary.com",
              FirstName = "Aroon",
              LastName = "Pahwa",
              GitHubUserName = "aroon",
            },
            Subscription = new ChargeBeeWebhookSubscription() {
              Status = "active",
              PlanId = "personal",
            },
            Card = new ChargeBeeWebhookCard() {
              ExpiryMonth = 9,
              ExpiryYear = 2016,
              Last4 = "5678",
            },
          },
        });
      await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);

      Assert.AreEqual(1, outgoingMessages.Count);
      var outgoingMessage = (CardExpiryReminderMailMessage)outgoingMessages.First();
      Assert.AreEqual("aroon@pureimaginary.com", outgoingMessage.ToAddress);
      Assert.AreEqual("Aroon Pahwa", outgoingMessage.ToName);
      Assert.AreEqual("aroon", outgoingMessage.GitHubUserName);
      Assert.AreEqual(true, outgoingMessage.AlreadyExpired);
      Assert.NotNull(outgoingMessage.UpdatePaymentMethodUrl);
      Assert.AreEqual("5678", outgoingMessage.LastCardDigits);
      Assert.AreEqual(9, outgoingMessage.ExpiryMonth);
      Assert.AreEqual(2016, outgoingMessage.ExpiryYear);
    }

    [Test]
    public async Task CardExpiryReminderSendsMessage() {
      var mockBusClient = new Mock<IShipHubQueueClient>();
      mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns(Task.CompletedTask);

      var outgoingMessages = new List<MailMessageBase>();
      var mockMailer = new Mock<IShipHubMailer>();
      mockMailer
        .Setup(x => x.CardExpiryReminder(It.IsAny<CardExpiryReminderMailMessage>()))
        .Returns(Task.CompletedTask)
        .Callback((CardExpiryReminderMailMessage message) => outgoingMessages?.Add(message));

      var controller = new Mock<ChargeBeeWebhookController>(Configuration, mockBusClient.Object, mockMailer.Object, null) {
        CallBase = true
      };

      var termEndDate = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero);

      ConfigureController(
        controller.Object,
        new ChargeBeeWebhookPayload() {
          EventType = "card_expiry_reminder",
          Content = new ChargeBeeWebhookContent() {
            Customer = new ChargeBeeWebhookCustomer() {
              Id = $"user-1234",
              Email = "aroon@pureimaginary.com",
              FirstName = "Aroon",
              LastName = "Pahwa",
              GitHubUserName = "aroon",
            },
            Subscription = new ChargeBeeWebhookSubscription() {
              Status = "active",
              PlanId = "personal",
            },
            Card = new ChargeBeeWebhookCard() {
              ExpiryMonth = 9,
              ExpiryYear = 2016,
              Last4 = "5678",
            },
          },
        });
      await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);

      Assert.AreEqual(1, outgoingMessages.Count);
      var outgoingMessage = (CardExpiryReminderMailMessage)outgoingMessages.First();
      Assert.AreEqual("aroon@pureimaginary.com", outgoingMessage.ToAddress);
      Assert.AreEqual("Aroon Pahwa", outgoingMessage.ToName);
      Assert.AreEqual("aroon", outgoingMessage.GitHubUserName);
      Assert.AreEqual(false, outgoingMessage.AlreadyExpired);
      Assert.NotNull(outgoingMessage.UpdatePaymentMethodUrl);
      Assert.AreEqual("5678", outgoingMessage.LastCardDigits);
      Assert.AreEqual(9, outgoingMessage.ExpiryMonth);
      Assert.AreEqual(2016, outgoingMessage.ExpiryYear);
    }

    [Test]
    public async Task CancellationScheduledSendsMessage() {
      var mockBusClient = new Mock<IShipHubQueueClient>();
      mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns(Task.CompletedTask);

      var outgoingMessages = new List<MailMessageBase>();
      var mockMailer = new Mock<IShipHubMailer>();
      mockMailer
        .Setup(x => x.CancellationScheduled(It.IsAny<CancellationScheduledMailMessage>()))
        .Returns(Task.CompletedTask)
        .Callback((CancellationScheduledMailMessage message) => outgoingMessages?.Add(message));

      var controller = new Mock<ChargeBeeWebhookController>(Configuration, mockBusClient.Object, mockMailer.Object, null) {
        CallBase = true
      };

      var termEndDate = new DateTimeOffset(2016, 11, 15, 0, 0, 0, TimeSpan.Zero);

      ConfigureController(
        controller.Object,
        new ChargeBeeWebhookPayload() {
          EventType = "subscription_cancellation_scheduled",
          Content = new ChargeBeeWebhookContent() {
            Customer = new ChargeBeeWebhookCustomer() {
              Id = $"user-1234",
              Email = "aroon@pureimaginary.com",
              FirstName = "Aroon",
              LastName = "Pahwa",
              GitHubUserName = "aroon",
            },
            Subscription = new ChargeBeeWebhookSubscription() {
              Status = "active",
              PlanId = "personal",
              CurrentTermEnd = termEndDate.ToUnixTimeSeconds(),
            },
          },
        });
      await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);

      Assert.AreEqual(1, outgoingMessages.Count);
      var outgoingMessage = (CancellationScheduledMailMessage)outgoingMessages.First();
      Assert.AreEqual("aroon@pureimaginary.com", outgoingMessage.ToAddress);
      Assert.AreEqual("Aroon Pahwa", outgoingMessage.ToName);
      Assert.AreEqual("aroon", outgoingMessage.GitHubUserName);
      Assert.AreEqual(termEndDate, outgoingMessage.CurrentTermEnd);
    }

    public static async Task CanIgnoreWebhookEventsViaSettingsHelper(
      string gitHubUserName,
      string includeOnlyList,
      string excludeList,
      bool expectToIgnore,
      string message
      ) {
      var mockBusClient = new Mock<IShipHubQueueClient>();
      var mockMailer = new Mock<IShipHubMailer>();

      var config = new ShipHubConfiguration() {
        ChargeBeeWebhookSecret = Configuration.ChargeBeeWebhookSecret,
      };
      if (includeOnlyList != null) {
        config.ChargeBeeWebhookIncludeOnlyList = includeOnlyList.Split(',').ToHashSet();
      }
      if (excludeList != null) {
        config.ChargeBeeWebhookExcludeList = excludeList.Split(',').ToHashSet();
      }

      var controller = new Mock<ChargeBeeWebhookController>(config, mockBusClient.Object, mockMailer.Object, null) {
        CallBase = true
      };

      ConfigureController(
        controller.Object,
        new ChargeBeeWebhookPayload() {
          EventType = "customer_deleted",
          Content = new ChargeBeeWebhookContent() {
            Customer = new ChargeBeeWebhookCustomer() {
              Id = "user-12345",
              GitHubUserName = "aroon",
            },
          },
        });

      var response = await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);
      if (expectToIgnore) {
        Assert.AreEqual(typeof(OkNegotiatedContentResult<string>), response.GetType(), message);
      } else {
        Assert.AreEqual(typeof(OkResult), response.GetType(), message);
      }
    }

    [Test]
    public async Task CanIgnoreWebhookEventsViaSettings() {
      await CanIgnoreWebhookEventsViaSettingsHelper("aroon", null, null, false, "should accept when include + exclude aren't set");
      await CanIgnoreWebhookEventsViaSettingsHelper("aroon", "foo,bar", null, true, "should reject since aroon not in include list.");
      await CanIgnoreWebhookEventsViaSettingsHelper("aroon", "foo,bar,aroon", null, false, "should accept since aroon is in the include list.");
      await CanIgnoreWebhookEventsViaSettingsHelper("aroon", null, "foo,bar", false, "should accept since aroon not in exclude list.");
      await CanIgnoreWebhookEventsViaSettingsHelper("aroon", null, "foo,bar,aroon", true, "should reject since aroon is in exclude list.");
    }

    [Test]
    public async Task ShouldIgnoreWebhookCheckDoesNotRunForIrrelevantEvents() {
      var mockBusClient = new Mock<IShipHubQueueClient>();
      var mockMailer = new Mock<IShipHubMailer>();

      var config = new ShipHubConfiguration() {
        ChargeBeeWebhookSecret = Configuration.ChargeBeeWebhookSecret,
      };
      var controller = new Mock<ChargeBeeWebhookController>(config, mockBusClient.Object, mockMailer.Object, null) {
        CallBase = true
      };

      controller
        .Setup(x => x.ShouldIgnoreWebhook(It.IsAny<ChargeBeeWebhookPayload>()))
        .Callback(() => {
          Assert.Fail("ShouldIgnoreWebhook should not be called - we don't care about this event.");
        });

      ConfigureController(
        controller.Object,
        new ChargeBeeWebhookPayload() {
          // We use "plan_updated" because it's an event type we do NOT handle
          // and therefore the ShouldIgnoreWebhook check should not run.
          EventType = "plan_updated",
          Content = new ChargeBeeWebhookContent() {
          },
        });

      var response = await controller.Object.HandleHook(Configuration.ChargeBeeWebhookSecret);
      Assert.AreEqual(typeof(OkResult), response.GetType());
    }
  }
}
