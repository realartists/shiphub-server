namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Linq;
  using System.Threading.Tasks;
  using Common.DataModel;
  using Filters;
  using Moq;
  using NUnit.Framework;
  using Sync;
  using Sync.Messages;
  using Sync.Messages.Entries;

  [TestFixture]
  [AutoRollback]
  public class SubscriptionEntrySyncTests {
    class Environment {
      public User user1;
      public User user2;
      public Organization org;
    }

    private static async Task<Environment> MakeEnvironment(ShipHubContext context) {
      Environment env = new Environment();
      env.user1 = TestUtil.MakeTestUser(context, 3001, "alok");
      env.user1.Token = Guid.NewGuid().ToString();
      env.user2 = TestUtil.MakeTestUser(context, 3002, "aroon");
      env.org = TestUtil.MakeTestOrg(context, 6001, "pureimaginary");

      await context.SetOrganizationUsers(
        env.org.Id,
        new[] {
            Tuple.Create(env.user1.Id, false),
        });

      await context.SaveChangesAsync();

      return env;
    }

    private static async Task<SubscriptionEntry> GetSubscriptionEntry(User user) {
      var logEntries = new List<SyncLogEntry>();

      var mockConnection = new Mock<ISyncConnection>();
      mockConnection
        .Setup(x => x.SendJsonAsync(It.IsAny<object>()))
        .Returns((object obj) => {
          var response = (SyncResponse)obj;
          Assert.IsInstanceOf<SyncResponse>(obj);
          logEntries.AddRange(response.Logs);
          return Task.CompletedTask;
        });

      var principal = new ShipHubPrincipal(user.Id, user.Login, user.Token);
      var syncContext = new SyncContext(principal, mockConnection.Object, new SyncVersions());
      await syncContext.Sync();

      var result = logEntries
        .Where(x => x.Entity == SyncEntityType.Subscription)
        .Select(x => (SubscriptionEntry)x.Data)
        .SingleOrDefault();
      Assert.IsNotNull(result, "Should have been sent a SubscriptionEntry.");

      return result;
    }

    [Test]
    public async Task ModeDefaultsToPaid() {
      using (var context = new ShipHubContext()) {
        Environment env = await MakeEnvironment(context);
        SubscriptionEntry entry = await GetSubscriptionEntry(env.user1);
        Assert.AreEqual(SubscriptionMode.Paid, entry.Mode,
          "If we haven't been able to fetch the data from ChargeBee yet, act as if paid.");
      }
    }

    [Test]
    public async Task ModeIsFreeWithNoSubscription() {
      using (var context = new ShipHubContext()) {
        Environment env = await MakeEnvironment(context);

        context.Subscriptions.Add(new Subscription() {
          AccountId = env.user1.Id,
          State = SubscriptionState.NotSubscribed,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = env.org.Id,
          State = SubscriptionState.NotSubscribed,
        });
        await context.SaveChangesAsync();

        SubscriptionEntry entry = await GetSubscriptionEntry(env.user1);
        Assert.AreEqual(SubscriptionMode.Free, entry.Mode,
          "Mode is free if nobody (personal or org) is paying.");
      }
    }

    [Test]
    public async Task ModeIsTrialWhenInTrial() {
      using (var context = new ShipHubContext()) {
        Environment env = await MakeEnvironment(context);

        context.Subscriptions.Add(new Subscription() {
          AccountId = env.user1.Id,
          State = SubscriptionState.InTrial,
        });
        await context.SaveChangesAsync();

        SubscriptionEntry entry = await GetSubscriptionEntry(env.user1);
        Assert.AreEqual(SubscriptionMode.Trial, entry.Mode,
          "We're in trial mode when user is in trial");
      }
    }

    [Test]
    public async Task ModeIsPaidWithPersonalSubscription() {
      using (var context = new ShipHubContext()) {
        Environment env = await MakeEnvironment(context);

        context.Subscriptions.Add(new Subscription() {
          AccountId = env.user1.Id,
          State = SubscriptionState.Subscribed,
        });
        await context.SaveChangesAsync();

        SubscriptionEntry entry = await GetSubscriptionEntry(env.user1);
        Assert.AreEqual(SubscriptionMode.Paid, entry.Mode,
          "Mode is paid with a personal subscription.");
      }
    }

    [Test]
    public async Task ModeIsPaidWithOrgSubscription() {
      using (var context = new ShipHubContext()) {
        Environment env = await MakeEnvironment(context);

        context.Subscriptions.Add(new Subscription() {
          AccountId = env.user1.Id,
          State = SubscriptionState.NotSubscribed,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = env.org.Id,
          State = SubscriptionState.Subscribed,
        });
        await context.SaveChangesAsync();

        SubscriptionEntry entry = await GetSubscriptionEntry(env.user1);
        Assert.AreEqual(SubscriptionMode.Paid, entry.Mode,
          "Mode is paid when an org pays.");
      }
    }

    [Test]
    public async Task ModeIsPaidWithOrgSubscriptionEvenIfUserHasTrial() {
      using (var context = new ShipHubContext()) {
        Environment env = await MakeEnvironment(context);

        context.Subscriptions.Add(new Subscription() {
          AccountId = env.user1.Id,
          State = SubscriptionState.InTrial,
        });
        context.Subscriptions.Add(new Subscription() {
          AccountId = env.org.Id,
          State = SubscriptionState.Subscribed,
        });
        await context.SaveChangesAsync();

        SubscriptionEntry entry = await GetSubscriptionEntry(env.user1);
        Assert.AreEqual(SubscriptionMode.Paid, entry.Mode,
          "A paid organization overrides a user's trial");
        Assert.IsNull(entry.TrialEndDate, "should not be set if we're in paid mode");
      }
    }

    [Test]
    public async Task TrialEndDateIsSetWhenUserIsInTrial() {
      using (var context = new ShipHubContext()) {
        Environment env = await MakeEnvironment(context);

        var trialEndDate = DateTimeOffset.Parse("10/1/2016 08:00:00 PM +00:00", null, DateTimeStyles.AssumeUniversal);

        context.Subscriptions.Add(new Subscription() {
          AccountId = env.user1.Id,
          State = SubscriptionState.InTrial,
          TrialEndDate = trialEndDate,
        });
        await context.SaveChangesAsync();

        SubscriptionEntry entry = await GetSubscriptionEntry(env.user1);
        Assert.AreEqual(SubscriptionMode.Trial, entry.Mode,
          "A paid organization overrides a user's trial");
        Assert.AreEqual(trialEndDate, entry.TrialEndDate);
      }
    }
  }
}
