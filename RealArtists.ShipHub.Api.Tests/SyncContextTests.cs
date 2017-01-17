namespace RealArtists.ShipHub.Api.Tests {
  using System;
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
  using Sync.Messages.Entries;

  [TestFixture]
  [AutoRollback]
  public class SyncContextTests {
    [Test]
    public async Task RepoShipNeedsWebhookHelpIsTrueWithNoHookAndNonAdminRole() {
      // We need help because 1) no hook is registered yet, and 2) the user is not
      // an admin and therefore we cannot add the hook for them.
      Assert.AreEqual(true, await RepoShipNeedsWebhookHelpHelper(hasHook: false, isAdmin: false));
    }

    [Test]
    public async Task RepoShipNeedsWebhookHelpIsFalseWithNoHookAndAdminRole() {
      // Don't need help because user is an admin.  Even if hook doesn't exist yet,
      // it will happen as part of the next sync.
      Assert.AreEqual(false, await RepoShipNeedsWebhookHelpHelper(hasHook: false, isAdmin: true));
    }

    [Test]
    public async Task RepoShipNeedsWebhookHelpIsFalseWhenHookIsPresent() {    
      // Don't need help - already have a hook.
      Assert.AreEqual(false, await RepoShipNeedsWebhookHelpHelper(hasHook: true, isAdmin: false));
    }

    private async Task<bool> RepoShipNeedsWebhookHelpHelper(bool hasHook, bool isAdmin) {
      using (var context = new ShipHubContext()) {
        var userAccount = new AccountTableType() {
          Id = 3001,
          Login = "aroon",
          Type = "user",
        };
        var orgAccount = new AccountTableType() {
          Id = 4001,
          Login = "pureimaginary",
          Type = "org",
        };
        var repo = new RepositoryTableType() {
          Id = 5001,
          AccountId = 4001,
          Name = "unicorns",
          FullName = "pureimaginary/unicorns",
          Private = true,
        };

        await context.BulkUpdateAccounts(DateTimeOffset.UtcNow, new[] { userAccount, orgAccount });
        var user = context.Accounts.Single(x => x.Id == userAccount.Id);
        user.Token = Guid.NewGuid().ToString();
        await context.SaveChangesAsync();

        await context.BulkUpdateRepositories(DateTimeOffset.UtcNow, new[] { repo });
        await context.SetOrganizationUsers(orgAccount.Id, new[] { Tuple.Create(user.Id, false) });

        await context.SetAccountLinkedRepositories(
          userAccount.Id,
          new[] { Tuple.Create(repo.Id, isAdmin) });

        if (hasHook) {
          context.Hooks.Add(new Hook() {
            GitHubId = 1234,
            RepositoryId = repo.Id,
            Secret = Guid.NewGuid(),
            Events = "someEvents",
          });
          await context.SaveChangesAsync();
        }

        var logs = new List<SyncLogEntry>();

        var mockConnection = new Mock<ISyncConnection>();
        mockConnection
          .Setup(x => x.SendJsonAsync(It.IsAny<object>()))
          .Returns((object obj) => {
            if (obj is SyncResponse) {
              var response = (SyncResponse)obj;
              logs.AddRange(response.Logs);
            }
            return Task.CompletedTask;
          });

        var principal = new ShipHubPrincipal(user.Id, user.Login, user.Token);
        var syncContext = new SyncContext(principal, mockConnection.Object, new SyncVersions());
        var changeSummary = new ChangeSummary();
        changeSummary.Add(userId: user.Id);
        await syncContext.Sync(changeSummary);

        // Bump RowVersion for this repo.
        await context.BumpRepositoryVersion(repo.Id);

        logs.Clear();
        await syncContext.Sync(changeSummary);

        return ((RepositoryEntry)logs.Single(x => x.Entity == SyncEntityType.Repository).Data).ShipNeedsWebhookHelp;
      }
    }

    [Test]
    public async Task OrgShipNeedsWebhookHelpIsTrueWithNoHookAndNonAdminRole() {
      // We need help because 1) no hook is registered yet, and 2) the user is not
      // an admin and therefore we cannot add the hook for them.
      Assert.AreEqual(true, await OrgShipNeedsWebhookHelpHelper(hasHook: false, isAdmin: false));
    }

    [Test]
    public async Task OrgShipNeedsWebhookHelpIsFalseWithNoHookAndAdminRole() {
      // Don't need help because user is an admin.  Even if hook doesn't exist yet,
      // it will happen as part of the next sync.
      Assert.AreEqual(false, await OrgShipNeedsWebhookHelpHelper(hasHook: false, isAdmin: true));
    }

    [Test]
    public async Task OrgShipNeedsWebhookHelpIsFalseWhenHookIsPresent() {
      // Don't need help - already have a hook.
      Assert.AreEqual(false, await OrgShipNeedsWebhookHelpHelper(hasHook: true, isAdmin: false));
    }

    private async Task<bool> OrgShipNeedsWebhookHelpHelper(bool hasHook, bool isAdmin) {
      using (var context = new ShipHubContext()) {
        var userAccount = new AccountTableType() {
          Id = 3001,
          Login = "aroon",
          Type = "user",
        };
        var orgAccount = new AccountTableType() {
          Id = 4001,
          Login = "pureimaginary",
          Type = "org",
        };
        
        await context.BulkUpdateAccounts(DateTimeOffset.UtcNow, new[] { userAccount, orgAccount });
        var user = context.Accounts.Single(x => x.Id == userAccount.Id);
        user.Token = Guid.NewGuid().ToString();

        if (hasHook) {
          context.Hooks.Add(new Hook() {
            GitHubId = 1234,
            OrganizationId = orgAccount.Id,
            Secret = Guid.NewGuid(),
            Events = "someEvents",
          });
        }
        await context.SaveChangesAsync();
        await context.SetUserOrganizations(user.Id, new[] { orgAccount.Id });
        await context.SetOrganizationUsers(orgAccount.Id, new[] { Tuple.Create(user.Id, isAdmin) });

        var logs = new List<SyncLogEntry>();

        var mockConnection = new Mock<ISyncConnection>();
        mockConnection
          .Setup(x => x.SendJsonAsync(It.IsAny<object>()))
          .Returns((object obj) => {
            if (obj is SyncResponse) {
              var response = (SyncResponse)obj;
              logs.AddRange(response.Logs);
            }
            return Task.CompletedTask;
          });

        var principal = new ShipHubPrincipal(user.Id, user.Login, user.Token);
        var syncContext = new SyncContext(principal, mockConnection.Object, new SyncVersions());
        var changeSummary = new ChangeSummary();
        changeSummary.Add(userId: user.Id);
        await syncContext.Sync(changeSummary);

        // Bump RowVersion for this org.
        await context.BumpOrganizationVersion(orgAccount.Id);

        logs.Clear();
        await syncContext.Sync(changeSummary);

        var orgEntry = logs
          .Where(x => x.Entity == SyncEntityType.Organization)
          .Select(x => x.Data)
          .OfType<OrganizationEntry>()
          .Single();
        return orgEntry.ShipNeedsWebhookHelp;
      }
    }

    [Test]
    public async Task AddingLabelstoIssueShouldSyncIssueAndRepo() {
      using (var context = new ShipHubContext()) {
        var userAccount = new AccountTableType() {
          Id = 3001,
          Login = "aroon",
          Type = "user",
        };
        var repo = new RepositoryTableType() {
          Id = 4001,
          AccountId = userAccount.Id,
          Name = "things",
          FullName = $"{userAccount.Login}/things",
          Private = true,
        };
        await context.BulkUpdateAccounts(DateTimeOffset.UtcNow, new[] { userAccount });
        await context.BulkUpdateRepositories(DateTimeOffset.UtcNow, new[] { repo });
        await context.SetAccountLinkedRepositories(userAccount.Id, new[] {
          Tuple.Create(repo.Id, true),
        });

        var issue = new IssueTableType() {
          Id = 5001,
          Number = 1,
          State = "open",
          Title = "Some Title",
          Body = "Some Body",
          UserId = userAccount.Id,
          CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        await context.BulkUpdateIssues(repo.Id, new[] { issue }, new MappingTableType[0], new MappingTableType[0]);

        var user = context.Accounts.Single(x => x.Id == userAccount.Id);
        user.Token = Guid.NewGuid().ToString();
        await context.SaveChangesAsync();

        var logs = new List<SyncLogEntry>();

        var mockConnection = new Mock<ISyncConnection>();
        mockConnection
          .Setup(x => x.SendJsonAsync(It.IsAny<object>()))
          .Returns((object obj) => {
            if (obj is SyncResponse) {
              var response = (SyncResponse)obj;
              logs.AddRange(response.Logs);
            }
            return Task.CompletedTask;
          });

        var principal = new ShipHubPrincipal(user.Id, user.Login, user.Token);
        var syncContext = new SyncContext(principal, mockConnection.Object, new SyncVersions());
        var changeSummary = new ChangeSummary();
        changeSummary.Add(userId: user.Id);
        await syncContext.Sync(changeSummary);
        logs.Clear();

        var labels = new[] {
          new LabelTableType() {
            Id = 6001,
            Name = "red",
            Color = "ff0000",
          },
          new LabelTableType() {
            Id = 6002,
            Name = "blue",
            Color = "0000ff",
          },
        };
        issue.UpdatedAt = issue.UpdatedAt.AddHours(1);
        // Adding some labels to an issue should trigger a new sync of the issue
        // and the repository.
        await context.BulkUpdateLabels(repo.Id, labels);
        await context.BulkUpdateIssues(
          repo.Id,
          new[] { issue },
          labels.Select(x => new MappingTableType() { Item1 = issue.Id, Item2 = x.Id }),
          new MappingTableType[0]);
        await syncContext.Sync(changeSummary);

        var labelEntries = logs.Where(x => x.Entity == SyncEntityType.Label).Select(x => (LabelEntry)x.Data);
        var repoLabels = labelEntries.Select(x => x.Identifier).OrderBy(x => x).ToArray();
        var issueEntry = logs.Where(x => x.Entity == SyncEntityType.Issue).Select(x => (IssueEntry)x.Data).Single();
        var issueLabels = issueEntry.Labels.OrderBy(x => x).ToArray();

        Assert.AreEqual(6001, repoLabels[0]);
        Assert.AreEqual(6002, repoLabels[1]);
        Assert.AreEqual(6001, issueLabels[0]);
        Assert.AreEqual(6002, issueLabels[1]);
      }
    }
  }
}
