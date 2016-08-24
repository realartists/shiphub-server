namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Linq;
  using System.Threading.Tasks;
  using Common.DataModel;
  using Common.DataModel.Types;
  using NUnit.Framework;

  [TestFixture]
  [AutoRollback]
  public class ShipHubContextTests {
    [Test]
    public async Task SetAccountLinkedRepositoriesCanSetAssociations() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo1 = TestUtil.MakeTestRepo(context, user.Id, 2001, "unicorns1");
        var repo2 = TestUtil.MakeTestRepo(context, user.Id, 2002, "unicorns2");
        await context.SaveChangesAsync();
        
        await context.SetAccountLinkedRepositories(user.Id, new[] {
          Tuple.Create(repo1.Id, false),
          Tuple.Create(repo2.Id, true),
        });

        var assocs = context.AccountRepositories
          .Where(x => x.AccountId == user.Id)
          .OrderBy(x => x.RepositoryId)
          .ToArray();
        Assert.AreEqual(2, assocs.Count());
        Assert.AreEqual(repo1.Id, assocs[0].RepositoryId);
        Assert.AreEqual(false, assocs[0].Admin);
        Assert.AreEqual(repo2.Id, assocs[1].RepositoryId);
        Assert.AreEqual(true, assocs[1].Admin);
      }
    }

    [Test]
    public async Task SetAccountLinkedRepositoriesCanDeleteAssociations() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo1 = TestUtil.MakeTestRepo(context, user.Id, 2001, "unicorns1");
        var repo2 = TestUtil.MakeTestRepo(context, user.Id, 2002, "unicorns2");
        await context.SaveChangesAsync();

        // Set a couple of associations...
        await context.SetAccountLinkedRepositories(user.Id, new[] {
          Tuple.Create(repo1.Id, false),
          Tuple.Create(repo2.Id, true),
        });

        // Then set again and omit repo1 to delete it.
        await context.SetAccountLinkedRepositories(user.Id, new[] {
          Tuple.Create(repo2.Id, true),
        });

        var assocs = context.AccountRepositories
          .Where(x => x.AccountId == user.Id)
          .OrderBy(x => x.RepositoryId)
          .ToArray();
        Assert.AreEqual(1, assocs.Count());
        Assert.AreEqual(repo2.Id, assocs[0].RepositoryId);
        Assert.AreEqual(true, assocs[0].Admin);
      }
    }

    [Test]
    public async Task SetAccountLinkedRepositoriesCanUpdateAssociations() {
      Account user;
      Repository repo1;
      Repository repo2;

      using (var context = new ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo1 = TestUtil.MakeTestRepo(context, user.Id, 2001, "unicorns1");
        repo2 = TestUtil.MakeTestRepo(context, user.Id, 2002, "unicorns2");
        await context.SaveChangesAsync();

        // Set a couple of associations...
        await context.SetAccountLinkedRepositories(user.Id, new[] {
          Tuple.Create(repo1.Id, false),
          Tuple.Create(repo2.Id, true),
        });

        var assocs = context.AccountRepositories
          .Where(x => x.AccountId == user.Id)
          .OrderBy(x => x.RepositoryId)
          .ToArray();
        Assert.AreEqual(2, assocs.Count());
        Assert.AreEqual(false, assocs[0].Admin);
        Assert.AreEqual(true, assocs[1].Admin);
      }

      using (var context = new ShipHubContext()) {
        // Then change the Admin bit on each.
        await context.SetAccountLinkedRepositories(user.Id, new[] {
          Tuple.Create(repo1.Id, true),
          Tuple.Create(repo2.Id, false),
        });
        
        var assocs = context.AccountRepositories
          .Where(x => x.AccountId == user.Id)
          .OrderBy(x => x.RepositoryId)
          .ToArray();
        Assert.AreEqual(2, assocs.Count());
        Assert.AreEqual(true, assocs[0].Admin);
        Assert.AreEqual(false, assocs[1].Admin);
      }
    }
  }
}
