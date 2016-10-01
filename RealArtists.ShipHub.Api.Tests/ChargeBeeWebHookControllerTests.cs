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
  using System.Web.Http.Routing;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Controllers;
  using Moq;
  using Newtonsoft.Json;
  using NUnit.Framework;
  using QueueClient;

  [TestFixture]
  [AutoRollback]
  public class ChargeBeeWebHookControllerTests {
    public static void ConfigureController(ChargeBeeWebhookController controller, ChargeBeeWebhookPayload payload) {
      var json = JsonConvert.SerializeObject(payload, GitHubSerialization.JsonSerializerSettings);

      var config = new HttpConfiguration();
      var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/webhook");
      request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
      var routeData = new HttpRouteData(config.Routes.MapHttpRoute("Chargebee", "chargebee"));

      controller.ControllerContext = new HttpControllerContext(config, routeData, request);
      controller.Request = request;
      controller.Request.Properties[HttpPropertyKeys.HttpConfigurationKey] = config;
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
      bool notifyExpected
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
          });
        } else {
          org = TestUtil.MakeTestOrg(context);
          sub = context.Subscriptions.Add(new Subscription() {
            AccountId = org.Id,
            State = beginState,
            TrialEndDate = beginTrialEndDate,
          });
        }

        await context.SaveChangesAsync();

        IChangeSummary changeSummary = null;
        var mockBusClient = new Mock<IShipHubQueueClient>();
        mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
          .Returns(Task.CompletedTask)
          .Callback((IChangeSummary arg) => { changeSummary = arg; });

        var controller = new ChargeBeeWebhookController(mockBusClient.Object);
        ConfigureController(
          controller,
          new ChargeBeeWebhookPayload() {
            EventType = eventType,
            Content = new ChargeBeeWebhookContent() {
              Customer = new ChargeBeeWebhookCustomer() {
                Id = $"{userOrOrg}-{sub.AccountId}",
              },
              Subscription = new ChargeBeeWebhookSubscription() {
                Status = chargeBeeState,
                TrialEnd = chargeBeeTrialEndDate?.ToUnixTimeSeconds(),
              }
            },
          });
        await controller.HandleHook();

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
        notifyExpected: true);        
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
        notifyExpected: true);
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
        notifyExpected: true);
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
        userOrOrg: "user",
        eventType: "subscription_created",
        chargeBeeState: "active",
        chargeBeeTrialEndDate: null,
        beginState: SubscriptionState.NotSubscribed,
        beginTrialEndDate: null,
        expectedState: SubscriptionState.Subscribed,
        expectedTrialEndDate: null,
        notifyExpected: true);
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
        notifyExpected: true);
    }
  }
}
