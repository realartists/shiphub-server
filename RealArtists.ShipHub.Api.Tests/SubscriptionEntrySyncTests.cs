namespace RealArtists.ShipHub.Api.Tests {
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Filters;
  using Moq;
  using NUnit.Framework;
  using Sync;
  using Sync.Messages;

  [TestFixture]
  [AutoRollback]
  public class SubscriptionEntrySyncTests {
    class Environment {
      public User user1;
      public User user2;
      public Organization org;
    }

    private static async Task<Environment> MakeEnvironment(ShipHubContext context) {
      var env = new Environment() {
        user1 = TestUtil.MakeTestUser(context, 3001, "alok"),
        user2 = TestUtil.MakeTestUser(context, 3002, "aroon"),
        org = TestUtil.MakeTestOrg(context, 6001, "pureimaginary")
      };
      await context.SetUserOrganizations(env.user1.Id, new[] { env.org.Id });
      await context.SaveChangesAsync();

      return env;
    }

    private static async Task<SubscriptionResponse> GetSubscriptionResponse(User user) {
      var messages = new List<SyncMessageBase>();

      var mockConnection = new Mock<ISyncConnection>();
      mockConnection
        .Setup(x => x.SendJsonAsync(It.IsAny<object>()))
        .Returns((object obj) => {
          Assert.IsInstanceOf<SyncMessageBase>(obj);
          messages.Add((SyncMessageBase)obj);
          return Task.CompletedTask;
        });

      var principal = new ShipHubPrincipal(user.Id, user.Login);
      var syncContext = new SyncContext(principal, mockConnection.Object, new SyncVersions());
      var changeSummary = new ChangeSummary();
      changeSummary.Add(userId: user.Id);
      await syncContext.Sync(changeSummary);

      var result = messages
        .Where(x => x.MessageType.Equals("subscription"))
        .SingleOrDefault();
      Assert.IsNotNull(result, "Should have been sent a SubscriptionEntry.");

      return (SubscriptionResponse)result;
    }

    [Test]
    public async Task ModeDefaultsToPaid() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);
        var response = await GetSubscriptionResponse(env.user1);
        Assert.AreEqual(SubscriptionMode.Paid, response.Mode,
          "If we haven't been able to fetch the data from ChargeBee yet, act as if paid.");
      }
    }

    [Test]
    public async Task ModeIsPaidWithPersonalSubscription() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        context.Subscriptions.Add(new Subscription() {
          AccountId = env.user1.Id,
          State = SubscriptionState.Subscribed,
        });
        await context.SaveChangesAsync();

        var entry = await GetSubscriptionResponse(env.user1);
        Assert.AreEqual(SubscriptionMode.Paid, entry.Mode,
          "Mode is paid with a personal subscription.");
      }
    }

    [Test]
    public async Task ModeIsPaidWithOrgSubscription() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        context.Subscriptions.Add(new Subscription() {
          AccountId = env.user1.Id,
          State = SubscriptionState.NotSubscribed,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = env.org.Id,
          State = SubscriptionState.Subscribed,
        });
        await context.SaveChangesAsync();

        var entry = await GetSubscriptionResponse(env.user1);
        Assert.AreEqual(SubscriptionMode.Paid, entry.Mode,
          "Mode is paid when an org pays.");
      }
    }

    [Test]
    public async Task ModeIsPaidWithOrgSubscriptionEvenIfUserHasTrial() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        context.Subscriptions.Add(new Subscription() {
          AccountId = env.user1.Id,
          State = SubscriptionState.InTrial,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = env.org.Id,
          State = SubscriptionState.Subscribed,
        });
        await context.SaveChangesAsync();

        var entry = await GetSubscriptionResponse(env.user1);
        Assert.AreEqual(SubscriptionMode.Paid, entry.Mode,
          "A paid organization overrides a user's trial");
        Assert.IsNull(entry.TrialEndDate, "should not be set if we're in paid mode");
      }
    }
  }
}
