namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;
  using Common.DataModel;
  using Common.DataModel.Types;
  using NUnit.Framework;

  [TestFixture]
  [AutoRollback]
  public class ShipHubContextTests {
    [Test]
    public async Task UsersCanBecomeOrgs() {
      using (var context = new ShipHubContext()) {
        var user1 = TestUtil.MakeTestUser(context, 3001, "user1");
        var user2 = TestUtil.MakeTestUser(context, 3002, "user2");
        var user3 = TestUtil.MakeTestUser(context, 3003, "user3");
        var repo1 = TestUtil.MakeTestRepo(context, user1.Id, 2001, "unicorns1");
        var repo2 = TestUtil.MakeTestRepo(context, user1.Id, 2002, "unicorns2");
        var repo3 = TestUtil.MakeTestRepo(context, user2.Id, 2003, "unicorns3");
        var repo4 = TestUtil.MakeTestRepo(context, user3.Id, 2004, "unicorns4");
        var org1 = TestUtil.MakeTestOrg(context, 6001, "org1");
        var org2 = TestUtil.MakeTestOrg(context, 6002, "org2");
        await context.SaveChangesAsync();

        await context.SetAccountLinkedRepositories(user1.Id, new[] {
          Tuple.Create(repo1.Id, false),
          Tuple.Create(repo2.Id, true),
        });

        await context.SetAccountLinkedRepositories(user2.Id, new[] {
          Tuple.Create(repo3.Id, false),
        });

        await context.SetAccountLinkedRepositories(user3.Id, new[] {
          Tuple.Create(repo3.Id, false),
          Tuple.Create(repo4.Id, false),
        });

        await context.SetUserOrganizations(user1.Id, new[] { org1.Id, org2.Id });

        // Make the user an org.
        var changes = await context.BulkUpdateAccounts(DateTimeOffset.UtcNow, new[] {
          new AccountTableType() {
            Id = user1.Id,
            Login = user1.Login,
            Type = "org",
          },
          new AccountTableType() {
            Id = user2.Id,
            Login = "user2Rename",
            Type = "user",
          },
          new AccountTableType(){
            Id = user3.Id,
            Login = user3.Login,
            Type = "user",
          }
        });

        // Should trigger organization change notifications for removed memberships
        Assert.IsTrue(changes.Organizations.SetEquals(new[] { org1.Id, org2.Id }));

        // Should trigger repository notifications
        Assert.IsTrue(changes.Repositories.SetEquals(new[] { repo1.Id, repo2.Id, repo3.Id }));

        // now we need a new context to defeat caching.
        using (var newContext = new ShipHubContext()) {
          // Should remove account repositories
          Assert.IsFalse(await newContext.AccountRepositories.Where(x => x.AccountId == user1.Id).AnyAsync());

          // Should clear token and rate limit
          var account = await newContext.Accounts.SingleAsync(x => x.Id == user1.Id);
          Assert.IsNull(account.Token);
          Assert.IsTrue(account.RateLimitRemaining == 0);

          // Should remove organization memberships
          Assert.IsFalse(await newContext.OrganizationAccounts.Where(x => x.UserId == user1.Id).AnyAsync());

          
        }
      }
    }

    [Test]
    public async Task OrgsCannotBecomeUsers() {
      using (var context = new ShipHubContext()) {
        var org1 = TestUtil.MakeTestOrg(context, 6001, "org1");
        await context.SaveChangesAsync();

        // (Try to) make the org a user
        var changes = await context.BulkUpdateAccounts(DateTimeOffset.UtcNow, new[] { new AccountTableType() {
          Id = org1.Id,
          Login = org1.Login,
          Type = "user",
        } });

        // Should be no changes
        Assert.IsTrue(changes.IsEmpty);

        // now we need a new context to defeat caching.
        using (var newContext = new ShipHubContext()) {
          Assert.IsNull(await newContext.Users.SingleOrDefaultAsync(x => x.Id == org1.Id));
        }
      }
    }

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

    [Test]
    public async Task SetOrganizationAdminsCanSetAssociations() {
      using (var context = new ShipHubContext()) {
        var user1 = TestUtil.MakeTestUser(context, userId: 3001, login: "aroon");
        var user2 = TestUtil.MakeTestUser(context, userId: 3002, login: "alok");
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        await context.SetOrganizationAdmins(org.Id, new[] { user1.Id, user2.Id });

        var assocs = context.OrganizationAccounts
          .Where(x => x.OrganizationId == org.Id)
          .OrderBy(x => x.UserId)
          .ToArray();
        Assert.AreEqual(2, assocs.Count());
        Assert.AreEqual(user1.Id, assocs[0].UserId);
        Assert.AreEqual(true, assocs[0].Admin);
        Assert.AreEqual(user2.Id, assocs[1].UserId);
        Assert.AreEqual(true, assocs[1].Admin);
      }
    }

    [Test]
    public async Task SetOrganizationAdminsDemotesThoseNotListedToUsers() {
      using (var context = new ShipHubContext()) {
        var user1 = TestUtil.MakeTestUser(context, userId: 3001, login: "aroon");
        var user2 = TestUtil.MakeTestUser(context, userId: 3002, login: "alok");
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        // Set a couple of associations...
        await context.SetOrganizationAdmins(org.Id, new[] { user1.Id, user2.Id });

        // Them set again and omit user1 to demote them from admin.
        await context.SetOrganizationAdmins(org.Id, new[] { user2.Id });

        var assocs = context.OrganizationAccounts
          .Where(x => x.OrganizationId == org.Id)
          .OrderBy(x => x.UserId)
          .ToArray();
        Assert.AreEqual(2, assocs.Count());
        Assert.AreEqual(user1.Id, assocs[0].UserId);
        Assert.AreEqual(false, assocs[0].Admin);
        Assert.AreEqual(user2.Id, assocs[1].UserId);
        Assert.AreEqual(true, assocs[1].Admin);
      }
    }

    [Test]
    public async Task SetOrganizationAdminsCanUpdateAdmins() {
      using (var context = new ShipHubContext()) {
        var user1 = TestUtil.MakeTestUser(context, userId: 3001, login: "aroon");
        var user2 = TestUtil.MakeTestUser(context, userId: 3002, login: "alok");
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        // Set a couple of associations...
        await context.SetUserOrganizations(user1.Id, new[] { org.Id });
        await context.SetUserOrganizations(user2.Id, new[] { org.Id });

        await context.SetOrganizationAdmins(org.Id, new[] { user2.Id });

        var assocs = context.OrganizationAccounts
          .Where(x => x.OrganizationId == org.Id)
          .OrderBy(x => x.UserId)
          .ToArray();
        var updated = assocs.ToDictionary(x => x.UserId, x => x.Admin);
        Assert.AreEqual(2, updated.Count);
        Assert.IsFalse(updated[user1.Id]);
        Assert.IsTrue(updated[user2.Id]);
      }
    }

    [Test]
    public async Task SetOrganizationAdminsCanUpdateAssociations() {
      Account user1;
      Account user2;
      Organization org;

      using (var context = new ShipHubContext()) {
        user1 = TestUtil.MakeTestUser(context, userId: 3001, login: "aroon");
        user2 = TestUtil.MakeTestUser(context, userId: 3002, login: "alok");
        org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        // Set a couple of associations...
        await context.SetUserOrganizations(user1.Id, new[] { org.Id });
        await context.SetOrganizationAdmins(org.Id, new[] { user2.Id });

        var assocs = context.OrganizationAccounts
          .Where(x => x.OrganizationId == org.Id)
          .OrderBy(x => x.UserId)
          .ToArray();
        Assert.AreEqual(2, assocs.Count());
        Assert.AreEqual(false, assocs[0].Admin);
        Assert.AreEqual(true, assocs[1].Admin);
      }

      using (var context = new ShipHubContext()) {
        // Then change the Admin bit on each.
        await context.SetOrganizationAdmins(org.Id, new[] { user1.Id });

        var assocs = context.OrganizationAccounts
          .Where(x => x.OrganizationId == org.Id)
          .OrderBy(x => x.UserId)
          .ToArray();
        Assert.AreEqual(2, assocs.Count());
        Assert.AreEqual(true, assocs[0].Admin);
        Assert.AreEqual(false, assocs[1].Admin);
      }
    }

    [Test]
    public async Task BulkUpdateLabels() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        await context.SaveChangesAsync();

        // Test that we can create some labels
        await context.BulkUpdateLabels(repo.Id, new[] {
          new LabelTableType() {
            Id = 1001,
            Name = "red",
            Color = "ff0000",
          },
          new LabelTableType() {
            Id = 1002,
            Name = "green",
            Color = "00ff00",
          },
        });
        var newRepo = context.Repositories.Single(x => x.Id == repo.Id);
        var labels = newRepo.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(2, labels.Count());
        Assert.AreEqual(1002, labels[0].Id);
        Assert.AreEqual("green", labels[0].Name);
        Assert.AreEqual("00ff00", labels[0].Color);
        Assert.AreEqual(1001, labels[1].Id);
        Assert.AreEqual("red", labels[1].Name);
        Assert.AreEqual("ff0000", labels[1].Color);

        // Test that we can update a label in place
        await context.BulkUpdateLabels(repo.Id, new[] {
          new LabelTableType() {
            Id = 1001,
            Name = "purple",
            Color = "ff00ff",
          },
        });
        Assert.AreEqual(2, context.Labels.Count(x => x.RepositoryId == repo.Id));
        var updatedLabel = context.Labels.Single(x => x.Id == 1001);
        context.Entry(updatedLabel).Reload();
        Assert.AreEqual("purple", updatedLabel.Name);
        Assert.AreEqual("ff00ff", updatedLabel.Color);

        // Test that we can delete labels with Complete = 1
        await context.BulkUpdateLabels(
          repo.Id,
          new[] {
            new LabelTableType() {
              Id = 1001,
              Name = "purple",
              Color = "ff00ff",
            },
          },
          complete: true);
        Assert.AreEqual(1, context.Labels.Count(x => x.RepositoryId == repo.Id));
        updatedLabel = context.Labels.Single(x => x.Id == 1001);
        context.Entry(updatedLabel).Reload();
        Assert.AreEqual("purple", updatedLabel.Name);

        // When we remove all labels for a repo, the IssueLabel linkage should also
        // go away.
        var issue = context.Issues.Add(new Issue() {
          Id = 2001,
          UserId = user.Id,
          RepositoryId = repo.Id,
          Number = 1,
          State = "open",
          Title = "Some Title",
          Body = "Some Body",
          CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        var someLabel = context.Labels.Single(x => x.Id == 1001);
        issue.Labels.Add(someLabel);
        await context.SaveChangesAsync();
        await context.BulkUpdateLabels(repo.Id, new LabelTableType[0], complete: true);

        using (var context2 = new ShipHubContext()) {
          var updatedIssue = context2.Issues.Single(x => x.Id == issue.Id);
          Assert.AreEqual(0, updatedIssue.Labels.Count());
        }
      }
    }

    [Test]
    public async Task BulkUpdateLabelsShouldNotDisturbIssueLabelRelationshipsInOtherRepos() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo1 = TestUtil.MakeTestRepo(context, user.Id, 2001, "repo1");
        var repo2 = TestUtil.MakeTestRepo(context, user.Id, 2002, "repo2");
        await context.SaveChangesAsync();

        await context.BulkUpdateLabels(repo1.Id, new[] {
          new LabelTableType() {
            Id = 1001,
            Name = "red",
            Color = "ff0000",
          },
        });
        await context.BulkUpdateLabels(repo2.Id, new[] {
          new LabelTableType() {
            Id = 1002,
            Name = "blue",
            Color = "0000ff",
          },
        });

        await context.BulkUpdateIssues(
          repo1.Id,
          new[] {
            new IssueTableType() {
              Id = 2001,
              Number = 1,
              State = "open",
              Title = "Some Title",
              Body = "Some Body",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
          },
          new[] {
            new MappingTableType() {
              Item1 = 2001,
              Item2 = 1001,
            },
          },
          new MappingTableType[0]);
        await context.BulkUpdateIssues(
          repo2.Id,
          new[] {
            new IssueTableType() {
              Id = 2002,
              Number = 1,
              State = "open",
              Title = "Some Title",
              Body = "Some Body",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
          },
          new[] {
            new MappingTableType() {
              Item1 = 2002,
              Item2 = 1002,
            },
          },
          new MappingTableType[0]);

        // Deleting all labels in repo1 should not disturb issue label relationships in repo2
        await context.BulkUpdateLabels(repo1.Id, new LabelTableType[0], complete: true);

        var repo2Issue = context.Issues.Single(x => x.Id == 2002);
        Assert.AreEqual(new long[] { 1002 }, repo2Issue.Labels.Select(x => x.Id).ToArray(),
          "IssueLabel relationship in other repo should be undisturbed");
      }
    }


    [Test]
    public async Task BulkUpdateIssuesMakesLabelAssociations() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id, 2001, "repo_a");
        var repo2 = TestUtil.MakeTestRepo(context, user.Id, 2002, "repo_b");
        context.Labels.Add(new Label() {
          Id = 4001,
          Name = "red2",
          Color = "ff0000",
          RepositoryId = repo2.Id,
        });
        await context.SaveChangesAsync();

        await context.BulkUpdateLabels(
          repo.Id,
          new[] {
            new LabelTableType() {
              Id = 2001,
              Name = "red",
              Color = "ff0000",
            },
            new LabelTableType() {
              Id = 2002,
              Name = "blue",
              Color = "0000ff",
            },
            new LabelTableType() {
              Id = 2003,
              Name = "blue",
              Color = "0000ff",
            },
          }
        );
        await context.BulkUpdateIssues(
          repo.Id,
          new[] {
            new IssueTableType() {
              Id = 1001,
              Number = 1,
              State = "open",
              Title = "Some Title",
              Body = "Some Body",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            new IssueTableType() {
              Id = 1002,
              Number = 2,
              State = "open",
              Title = "Some Title 2",
              Body = "Some Body 2",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
          },
          new[] {
            new MappingTableType() {
              Item1 = 1001,
              Item2 = 2001,
            },
            new MappingTableType() {
              Item1 = 1001,
              Item2 = 2002,
            },
            new MappingTableType() {
              Item1 = 1002,
              Item2 = 2003,
            },
          },
          new MappingTableType[0]);

        Assert.AreEqual(4, context.Labels.Count(),
          "should have 3 repos: 3 in repo1, 1 in repo2");
        var labels = context.Labels.Where(x => x.RepositoryId == repo.Id).OrderBy(x => x.Id).ToArray();
        Assert.AreEqual(2001, labels[0].Id);
        Assert.AreEqual("red", labels[0].Name);
        Assert.AreEqual("ff0000", labels[0].Color);
        Assert.AreEqual(2002, labels[1].Id);
        Assert.AreEqual("blue", labels[1].Name);
        Assert.AreEqual("0000ff", labels[1].Color);
        Assert.AreEqual(2003, labels[2].Id);
        Assert.AreEqual("blue", labels[2].Name);
        Assert.AreEqual("0000ff", labels[2].Color);

        var issue = context.Issues.Single(x => x.Id == 1001);
        Assert.AreEqual(
          new long[] { 2001, 2002 },
          issue.Labels.OrderBy(x => x.Id).Select(x => x.Id).ToArray(),
          "issue should only be linked with labels which named this issue (IssueId = 1001)");
      }
    }

    [Test]
    public async Task BulkUpdateIssuesCanAssignTheSameLabelToMultipleIssues() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id, 2001, "repo_a");
        await context.SaveChangesAsync();

        await context.BulkUpdateLabels(
          repo.Id,
          new[] {
            new LabelTableType() {
              Id = 2001,
              Name = "red",
              Color = "ff0000",
            },
          });

        await context.BulkUpdateIssues(
          repo.Id,
          new[] {
            new IssueTableType() {
              Id = 1001,
              Number = 1,
              State = "open",
              Title = "Some Title",
              Body = "Some Body",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            new IssueTableType() {
              Id = 1002,
              Number = 2,
              State = "open",
              Title = "Some Title 2",
              Body = "Some Body 2",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
          },
          new[] {
            new MappingTableType() {
              Item1 = 1001,
              Item2 = 2001,
            },
            new MappingTableType() {
              Item1 = 1002,
              Item2 = 2001,
            },
          },
          new MappingTableType[0]);

        var issue1 = context.Issues.Single(x => x.Id == 1001);
        var issue2 = context.Issues.Single(x => x.Id == 1002);
        Assert.AreEqual(new long[] { 2001 }, issue1.Labels.Select(x => x.Id).ToArray());
        Assert.AreEqual(new long[] { 2001 }, issue2.Labels.Select(x => x.Id).ToArray());
      }
    }

    [Test]
    public async Task BulkUpdateIssuesPreservesExistingIssueLabelAssociations() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id, 2001, "repo_a");
        await context.SaveChangesAsync();

        await context.BulkUpdateLabels(repo.Id,
          new[] {
            new LabelTableType() {
              Id = 2001,
              Name = "red",
              Color = "ff0000",
            },
            new LabelTableType() {
              Id = 2003,
              Name = "blue",
              Color = "0000ff",
            },
          });

        await context.BulkUpdateIssues(
          repo.Id,
          new[] {
            new IssueTableType() {
              Id = 1001,
              Number = 1,
              State = "open",
              Title = "Some Title",
              Body = "Some Body",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            new IssueTableType() {
              Id = 1002,
              Number = 2,
              State = "open",
              Title = "Some Title 2",
              Body = "Some Body 2",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
          },
          new[] {
            new MappingTableType() {
              Item1 = 1001,
              Item2 = 2001,
            },
            new MappingTableType() {
              Item1 = 1002,
              Item2 = 2003,
            },
          },
          new MappingTableType[0]);

        // Call BulkUpdateIssue again, but only with the second issue.
        await context.BulkUpdateIssues(
          repo.Id,
          new[] {
            new IssueTableType() {
              Id = 1002,
              Number = 2,
              State = "open",
              Title = "Some Title 2",
              Body = "Some Body 2",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
          },
          new[] {
            new MappingTableType() {
              Item1 = 1002,
              Item2 = 2003,
            },
          },
          new MappingTableType[0]);

        // Only issue 1002 is named in the second BulkUpdateIssues call, and so none
        // of the labels (i.e. IssueLabels associations) for the other issue (1001)
        // should be disturbed.
        Assert.AreEqual(2, context.Labels.Count());
      };
    }

    [Test]
    public async Task SetIssueTemplateToNull() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id, 2001, "repo_a");
        await context.SaveChangesAsync();

        await context.SetRepositoryIssueTemplate(repo.Id, null);
        await context.SaveChangesAsync();

        var updatedRepo1 = context.Repositories.Single(x => x.Id == repo.Id);
        Assert.Null(updatedRepo1.IssueTemplate);
      }
    }

    [Test]
    public async Task BulkUpdateIssuesDoesNotDistrurbIssueLabelAssociationsInOtherRepos() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id, 2001, "repo_a");
        var repo2 = TestUtil.MakeTestRepo(context, user.Id, 2002, "repo_b");
        await context.SaveChangesAsync();

        await context.BulkUpdateLabels(
          repo.Id,
          new[] {
            new LabelTableType() {
              Id = 2001,
              Name = "red",
              Color = "ff0000",
            },
          });

        await context.BulkUpdateIssues(
          repo.Id,
          new[] {
            new IssueTableType() {
              Id = 1001,
              Number = 1,
              State = "open",
              Title = "Some Title",
              Body = "Some Body",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
          },
          new[] {
            new MappingTableType() {
              Item1 = 1001,
              Item2 = 2001,
            },
          },
          new MappingTableType[0]);

        await context.BulkUpdateLabels(
          repo2.Id,
          new[] {
            new LabelTableType() {
              Id = 2201,
              Name = "red",
              Color = "ff0000",
            },
          });

        await context.BulkUpdateIssues(
          repo2.Id,
          new[] {
            new IssueTableType() {
              Id = 1201,
              Number = 1,
              State = "open",
              Title = "Some Title",
              Body = "Some Body",
              UserId = user.Id,
              CreatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
              UpdatedAt = new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
          },
          new[] {
            new MappingTableType() {
              Item1 = 1201,
              Item2 = 2201,
            },
          },
          new MappingTableType[0]);

        var updatedRepo1 = context.Repositories.Single(x => x.Id == repo.Id);
        var updatedRepo2 = context.Repositories.Single(x => x.Id == repo2.Id);
        Assert.AreEqual(new long[] { 2001 }, updatedRepo1.Labels.Select(x => x.Id).ToArray());
        Assert.AreEqual(new long[] { 2201 }, updatedRepo2.Labels.Select(x => x.Id).ToArray());
      };
    }
  }
}
