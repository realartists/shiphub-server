namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Common.DataModel;
  using Common.GitHub;
  using Moq;
  using NUnit.Framework;
  using QueueProcessor;

  [TestFixture]
  [AutoRollback]
  public class WebhookReaperTests {
    private static Mock<WebhookReaper> MockReaper(
      Dictionary<string, List<Tuple<string, string, long>>> pings) {
      var mock = new Mock<WebhookReaper>() { CallBase = true };
      mock
        .Setup(x => x.CreateGitHubClient(It.IsAny<User>()))
        .Returns((User user) => {
          if (!pings.ContainsKey(user.Token)) {
            pings[user.Token] = new List<Tuple<string, string, long>>();
          }

          var mockClient = new Mock<IGitHubClient>();

          mockClient
            .Setup(x => x.PingRepositoryWebhook(It.IsAny<string>(), It.IsAny<long>()))
            .Returns((string repoFullName, long hookId) => {
              pings[user.Token].Add(Tuple.Create("repo", repoFullName, hookId));
              return Task.FromResult(new GitHubResponse<bool>(null) {
                Result = true,
              });
            });

          mockClient
              .Setup(x => x.PingOrganizationWebhook(It.IsAny<string>(), It.IsAny<long>()))
              .Returns((string name, long hookId) => {
                pings[user.Token].Add(Tuple.Create("org", name, hookId));
                return Task.FromResult(new GitHubResponse<bool>(null) {
                  Result = true,
                });
              });

          return mockClient.Object;
        });

      return mock;
    }

    class Environment {
      public User user1;
      public User user2;
      public Organization org1;
      public Organization org2;
      public Hook org1Hook;
      public Hook org2Hook;
      public Repository repo1;
      public Repository repo2;
      public Hook repo1Hook;
      public Hook repo2Hook;
    }

    private static async Task<Environment> MakeEnvironment(ShipHubContext context) {
      var env = new Environment();
      env.user1 = TestUtil.MakeTestUser(context, 3001, "aroon");
      env.user2 = TestUtil.MakeTestUser(context, 3002, "alok");

      env.org1 = TestUtil.MakeTestOrg(context, 6001, "myorg1");
      env.org2 = TestUtil.MakeTestOrg(context, 6002, "myorg2");

      env.repo1 = TestUtil.MakeTestRepo(context, env.org1.Id, 2001, "unicorns");
      env.repo2 = TestUtil.MakeTestRepo(context, env.org1.Id, 2002, "girafficorns");

      env.repo1Hook = context.Hooks.Add(new Hook() {
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        RepositoryId = env.repo1.Id,
      });
      env.repo2Hook = context.Hooks.Add(new Hook() {
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        RepositoryId = env.repo2.Id,
      });

      env.org1Hook = context.Hooks.Add(new Hook() {
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        OrganizationId = env.org1.Id,
      });
      env.org2Hook = context.Hooks.Add(new Hook() {
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        OrganizationId = env.org2.Id,
      });

      // Make both users admins of all repos + orgs.
      await context.SetAccountLinkedRepositories(env.user1.Id, new[] {
        Tuple.Create(env.repo1.Id, true),
        Tuple.Create(env.repo2.Id, true),
      });
      await context.SetAccountLinkedRepositories(env.user2.Id, new[] {
        Tuple.Create(env.repo1.Id, true),
        Tuple.Create(env.repo2.Id, true),
      });
      await context.SetOrganizationUsers(env.org1.Id, new[] {
        Tuple.Create(env.user1.Id, true),
        Tuple.Create(env.user2.Id, true),
      });
      await context.SetOrganizationUsers(env.org2.Id, new[] {
        Tuple.Create(env.user1.Id, true),
        Tuple.Create(env.user2.Id, true),
      });
      await context.SaveChangesAsync();

      return env;
    }

    [Test]
    public async Task WillPingHooksNotSeenInMoreThan24Hours() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        // Pretend repo1's hook is stale (last seen > 24 hours ago)
        env.repo1Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-25);
        // Pretend repo2's hook is fresh (last seen < 24 hours ago)
        env.repo2Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-23);

        // Pretend org2's hook is stale, while org1's is fresh.
        env.org1Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-23);
        env.org2Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-25);

        await context.SaveChangesAsync();

        var pings = new Dictionary<string, List<Tuple<string, string, long>>>();
        var mock = MockReaper(pings);

        await mock.Object.Run();

        context.Entry(env.repo1Hook).Reload();
        context.Entry(env.org2Hook).Reload();
        Assert.AreEqual(1, env.repo1Hook.PingCount);
        Assert.AreEqual(1, env.org2Hook.PingCount);
        Assert.AreEqual(new[] { env.user1.Token }, pings.Keys.ToArray());
        Assert.AreEqual(
          new[] {
            Tuple.Create("repo", "aroon/unicorns", env.repo1Hook.Id),
            Tuple.Create("org", "myorg2", env.org2Hook.Id),
          },
          pings[env.user1.Token].ToArray());

        // If we run again sooner than 30 minutes from now, we should not
        // see pings.  (The threshold for re-pinging is 30+ minutes)
        pings.Clear();
        mock.Setup(x => x.UtcNow).Returns(DateTimeOffset.UtcNow.AddMinutes(29));
        await mock.Object.Run();
        context.Entry(env.repo1Hook).Reload();
        context.Entry(env.org2Hook).Reload();
        Assert.AreEqual(1, env.repo1Hook.PingCount);
        Assert.AreEqual(1, env.org2Hook.PingCount);

        // But, if more than 30 minutes passes, the ping count should keep
        // going up if we run again.
        pings.Clear();
        mock.Setup(x => x.UtcNow).Returns(DateTimeOffset.UtcNow.AddMinutes(31));
        await mock.Object.Run();
        context.Entry(env.repo1Hook).Reload();
        context.Entry(env.org2Hook).Reload();
        Assert.AreEqual(2, env.repo1Hook.PingCount);
        Assert.AreEqual(2, env.org2Hook.PingCount);

      }
    }

    [Test]
    public async Task WillNotPingOrgHooksWhenWeCannotFindAdmins() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        // org1's hook is stale; org2's hook is fresh.
        env.org1Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-25);
        env.org2Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-23);
        await context.SaveChangesAsync();

        // No admins!  We'd never be able to make the ping request
        // to GitHub.
        await context.SetOrganizationUsers(env.org1.Id, new[] {
          Tuple.Create(env.user1.Id, false),
          Tuple.Create(env.user2.Id, false),
        });
        await context.SetOrganizationUsers(env.org2.Id, new[] {
          Tuple.Create(env.user1.Id, false),
          Tuple.Create(env.user2.Id, false),
        });

        var pings = new Dictionary<string, List<Tuple<string, string, long>>>();
        var mock = MockReaper(pings);

        await mock.Object.Run();

        context.Entry(env.org1Hook).Reload();
        Assert.AreEqual(1, env.org1Hook.PingCount);
        Assert.AreEqual(0, pings.Keys.Count);
      }
    }

    [Test]
    public async Task WillNotPingRepoHooksWhenWeCannotFindAdmins() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        env.repo1Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-25);
        env.repo1Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-25);

        await context.SaveChangesAsync();

        await context.SetAccountLinkedRepositories(env.user1.Id, new[] {
          Tuple.Create(env.repo1.Id, false),
          Tuple.Create(env.repo2.Id, false),
        });
        await context.SetAccountLinkedRepositories(env.user2.Id, new[] {
          Tuple.Create(env.repo1.Id, false),
          Tuple.Create(env.repo2.Id, false),
        });

        var pings = new Dictionary<string, List<Tuple<string, string, long>>>();
        var mock = MockReaper(pings);

        await mock.Object.Run();

        context.Entry(env.repo1Hook).Reload();
        Assert.AreEqual(1, env.repo1Hook.PingCount);
      }
    }

    [Test]
    public async Task WillDeleteHooksWhenNoPongIsHeardAfter3Pings() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        // Pretend all hooks are stale...
        env.repo1Hook.LastSeen = DateTimeOffset.UtcNow.AddDays(-2);
        env.repo2Hook.LastSeen = DateTimeOffset.UtcNow.AddDays(-2);
        env.org1Hook.LastSeen = DateTimeOffset.UtcNow.AddDays(-2);
        env.org2Hook.LastSeen = DateTimeOffset.UtcNow.AddDays(-2);

        // These should not get deleted, since they've only failed twice.
        env.repo2Hook.PingCount = 2;
        env.org1Hook.PingCount = 2;

        // These should get deleted because they've failed 3 or more times.
        env.repo1Hook.PingCount = 3;
        env.org2Hook.PingCount = 3;

        await context.SaveChangesAsync();

        var pings = new Dictionary<string, List<Tuple<string, string, long>>>();
        var mock = MockReaper(pings);
        await mock.Object.Run();

        var remainingHookIds = context.Hooks.Select(x => x.Id).ToArray();
        Assert.AreEqual(new[] { env.repo2Hook.Id, env.org1Hook.Id }, remainingHookIds);
      }
    }

  }
}
