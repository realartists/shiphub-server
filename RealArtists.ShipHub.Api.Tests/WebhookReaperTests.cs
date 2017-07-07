namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common.DataModel;
  using Common.GitHub;
  using Microsoft.Azure.WebJobs;
  using Moq;
  using NUnit.Framework;
  using QueueProcessor.Jobs;
  using QueueProcessor.Tracing;
  using RealArtists.ShipHub.Common;
  using RealArtists.ShipHub.QueueClient.Messages;

  [TestFixture]
  [AutoRollback]
  public class WebhookReaperTests {
    private static Mock<WebhookReaperTimer> MockReaper(
      Dictionary<long, List<(string hookType, string resourceName, long hookId)>> pings) {

      var mockGrainFactory = new Mock<IAsyncGrainFactory>();
      mockGrainFactory.Setup(x => x.GetGrain<IGitHubActor>(It.IsAny<long>(), It.IsAny<string>()))
        .Returns((long userId, string _) => {
          if (!pings.ContainsKey(userId)) {
            pings[userId] = new List<(string, string, long)>();
          }

          var mockClient = new Mock<IGitHubActor>();

          mockClient
            .Setup(x => x.PingRepositoryWebhook(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<RequestPriority>()))
            .Returns((string repoFullName, long hookId, RequestPriority priority) => {
              pings[userId].Add(("repo", repoFullName, hookId));
              return Task.FromResult(new GitHubResponse<bool>(null) {
                Result = true,
              });
            });

          mockClient
              .Setup(x => x.PingOrganizationWebhook(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<RequestPriority>()))
              .Returns((string name, long hookId, RequestPriority priority) => {
                pings[userId].Add(("org", name, hookId));
                return Task.FromResult(new GitHubResponse<bool>(null) {
                  Result = true,
                });
              });

          return Task.FromResult(mockClient.Object);
        });

      var mock = new Mock<WebhookReaperTimer>(mockGrainFactory.Object, new DetailedExceptionLogger()) { CallBase = true };

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
      var env = new Environment() {
        user1 = TestUtil.MakeTestUser(context, 3001, "aroon"),
        user2 = TestUtil.MakeTestUser(context, 3002, "alok"),

        org1 = TestUtil.MakeTestOrg(context, 6001, "myorg1"),
        org2 = TestUtil.MakeTestOrg(context, 6002, "myorg2")
      };

      env.repo1 = TestUtil.MakeTestRepo(context, env.org1.Id, 2001, "unicorns");
      env.repo2 = TestUtil.MakeTestRepo(context, env.org1.Id, 2002, "girafficorns");

      env.repo1Hook = context.Hooks.Add(new Hook() {
        GitHubId = 5551,
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        RepositoryId = env.repo1.Id,
      });
      env.repo2Hook = context.Hooks.Add(new Hook() {
        GitHubId = 5552,
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        RepositoryId = env.repo2.Id,
      });

      env.org1Hook = context.Hooks.Add(new Hook() {
        GitHubId = 6661,
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        OrganizationId = env.org1.Id,
      });
      env.org2Hook = context.Hooks.Add(new Hook() {
        GitHubId = 6662,
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        OrganizationId = env.org2.Id,
      });

      // Make both users admins of all repos + orgs.
      await context.SetAccountLinkedRepositories(env.user1.Id, new[] {
        (env.repo1.Id, true),
        (env.repo2.Id, true),
      });
      await context.SetAccountLinkedRepositories(env.user2.Id, new[] {
        (env.repo1.Id, true),
        (env.repo2.Id, true),
      });
      await context.SetUserOrganizations(env.user1.Id, new[] { env.org1.Id, env.org2.Id });
      await context.SetUserOrganizations(env.user2.Id, new[] { env.org1.Id, env.org2.Id });
      await context.SetOrganizationAdmins(env.org1.Id, new[] {
        env.user1.Id,
        env.user2.Id,
      });
      await context.SetOrganizationAdmins(env.org2.Id, new[] {
        env.user1.Id,
        env.user2.Id,
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

        var pings = new Dictionary<long, List<(string, string, long)>>();
        var mock = MockReaper(pings);
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();

        await mock.Object.Run(collectorMock.Object);

        context.Entry(env.repo1Hook).Reload();
        context.Entry(env.org2Hook).Reload();
        Assert.AreEqual(1, env.repo1Hook.PingCount);
        Assert.AreEqual(1, env.org2Hook.PingCount);
        Assert.AreEqual(new[] { env.user1.Id }, pings.Keys.ToArray());
        Assert.AreEqual(
          new[] {
            ("repo", "myorg1/unicorns", (long)env.repo1Hook.GitHubId),
            ("org", "myorg2", (long)env.org2Hook.GitHubId),
          },
          pings[env.user1.Id].ToArray());

        // If we run again sooner than 30 minutes from now, we should not
        // see pings.  (The threshold for re-pinging is 30+ minutes)
        pings.Clear();
        mock.Setup(x => x.UtcNow).Returns(DateTimeOffset.UtcNow.AddMinutes(29));
        await mock.Object.Run(collectorMock.Object);
        context.Entry(env.repo1Hook).Reload();
        context.Entry(env.org2Hook).Reload();
        Assert.AreEqual(1, env.repo1Hook.PingCount);
        Assert.AreEqual(1, env.org2Hook.PingCount);

        // But, if more than 30 minutes passes, the ping count should keep
        // going up if we run again.
        pings.Clear();
        mock.Setup(x => x.UtcNow).Returns(DateTimeOffset.UtcNow.AddMinutes(31));
        await mock.Object.Run(collectorMock.Object);
        context.Entry(env.repo1Hook).Reload();
        context.Entry(env.org2Hook).Reload();
        Assert.AreEqual(2, env.repo1Hook.PingCount);
        Assert.AreEqual(2, env.org2Hook.PingCount);
      }
    }

    [Test]
    public async Task WillMarkOrgHooksPingedWhenWeCannotFindAdmins() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        // org1's hook is stale; org2's hook is fresh.
        env.org1Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-25);
        env.org2Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-23);
        await context.SaveChangesAsync();

        // No admins!  We'd never be able to make the ping request
        // to GitHub.
        await context.SetOrganizationAdmins(env.org1.Id, Array.Empty<long>());
        await context.SetOrganizationAdmins(env.org2.Id, Array.Empty<long>());

        var pings = new Dictionary<long, List<(string, string, long)>>();
        var mock = MockReaper(pings);
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();

        await mock.Object.Run(collectorMock.Object);

        // Pinged
        context.Entry(env.org1Hook).Reload();
        Assert.AreEqual(1, env.org1Hook.PingCount);

        // Not pinged
        context.Entry(env.org2Hook).Reload();
        Assert.IsNull(env.org2Hook.PingCount);

        // We didn't actually ping them
        Assert.False(pings.ContainsKey(env.org1Hook.Id));
        Assert.False(pings.ContainsKey(env.org2Hook.Id));
      }
    }

    [Test]
    public async Task WillMarkOrgHooksPingedWhenWeCannotFindAdminsWithTokens() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        // org1's hook is stale; org2's hook is fresh.
        env.org1Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-25);
        env.org2Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-23);

        await context.SaveChangesAsync();

        // No tokens!
        await context.RevokeAccessTokens(env.user1.Id);
        await context.RevokeAccessTokens(env.user2.Id);

        // Both users are admins.
        await context.SetOrganizationAdmins(env.org1.Id, new[] {
          env.user1.Id,
          env.user2.Id,
        });
        await context.SetOrganizationAdmins(env.org2.Id, new[] {
          env.user1.Id,
          env.user2.Id,
        });

        var pings = new Dictionary<long, List<(string, string, long)>>();
        var mock = MockReaper(pings);
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();

        await mock.Object.Run(collectorMock.Object);

        // Pinged
        context.Entry(env.org1Hook).Reload();
        Assert.AreEqual(1, env.org1Hook.PingCount);

        // Not pinged
        context.Entry(env.org2Hook).Reload();
        Assert.IsNull(env.org2Hook.PingCount);

        // We didn't actually ping them
        Assert.False(pings.ContainsKey(env.org1Hook.Id));
        Assert.False(pings.ContainsKey(env.org2Hook.Id));
      }
    }

    [Test]
    public async Task WillRepoHooksPingedWhenWeCannotFindAdmins() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        // repo1's hook is stale; repo2's hook is fresh.
        env.repo1Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-25);
        env.repo2Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-23);

        await context.SaveChangesAsync();

        await context.SetAccountLinkedRepositories(env.user1.Id, new[] {
          (env.repo1.Id, false),
          (env.repo2.Id, false),
        });
        await context.SetAccountLinkedRepositories(env.user2.Id, new[] {
          (env.repo1.Id, false),
          (env.repo2.Id, false),
        });

        var pings = new Dictionary<long, List<(string, string, long)>>();
        var mock = MockReaper(pings);
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();

        await mock.Object.Run(collectorMock.Object);

        // Pinged
        context.Entry(env.repo1Hook).Reload();
        Assert.AreEqual(1, env.repo1Hook.PingCount);

        // Not Pinged
        context.Entry(env.repo2Hook).Reload();
        Assert.IsNull(env.repo2Hook.PingCount);

        // We didn't actually ping them
        Assert.False(pings.ContainsKey(env.repo1Hook.Id));
        Assert.False(pings.ContainsKey(env.repo2Hook.Id));
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

        var pings = new Dictionary<long, List<(string, string, long)>>();
        var mock = MockReaper(pings);

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        var changedOrgs = new HashSet<long>();
        var changedRepos = new HashSet<long>();
        collectorMock
          .Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage changeMessage, CancellationToken token) => {
            changedOrgs.UnionWith(changeMessage.Organizations);
            changedRepos.UnionWith(changeMessage.Repositories);
            return Task.CompletedTask;
          });

        await mock.Object.Run(collectorMock.Object);

        var remainingHookIds = context.Hooks.Select(x => x.Id).ToArray();
        Assert.AreEqual(new[] { env.repo2Hook.Id, env.org1Hook.Id }, remainingHookIds);
        Assert.AreEqual(new[] { env.repo1Hook.RepositoryId }, changedRepos.ToArray());
        Assert.AreEqual(new[] { env.org2Hook.OrganizationId }, changedOrgs.ToArray());
      }
    }

    [Test]
    public async Task WillMarkRepoHooksPingedWhenWeCannotFindAdminsWithTokens() {
      using (var context = new ShipHubContext()) {
        var env = await MakeEnvironment(context);

        // repo1's hook is stale; repo2's hook is fresh.
        env.repo1Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-25);
        env.repo2Hook.LastSeen = DateTimeOffset.UtcNow.AddHours(-23);

        await context.SaveChangesAsync();

        // No tokens!
        await context.RevokeAccessTokens(env.user1.Id);
        await context.RevokeAccessTokens(env.user2.Id);

        // Both are admins.
        await context.SetAccountLinkedRepositories(env.user1.Id, new[] {
          (env.repo1.Id, true),
          (env.repo2.Id, true),
        });
        await context.SetAccountLinkedRepositories(env.user2.Id, new[] {
          (env.repo1.Id, true),
          (env.repo2.Id, true),
        });

        var pings = new Dictionary<long, List<(string, string, long)>>();
        var mock = MockReaper(pings);
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();

        await mock.Object.Run(collectorMock.Object);

        // Pinged
        context.Entry(env.repo1Hook).Reload();
        Assert.AreEqual(1, env.repo1Hook.PingCount);

        // Not Pinged
        context.Entry(env.repo2Hook).Reload();
        Assert.IsNull(env.repo2Hook.PingCount);

        // We didn't actually ping them
        Assert.False(pings.ContainsKey(env.repo1Hook.Id));
        Assert.False(pings.ContainsKey(env.repo2Hook.Id));
      }
    }
  }
}
