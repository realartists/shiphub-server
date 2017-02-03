namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Collections.Immutable;
  using System.Linq;
  using System.Net;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Microsoft.Azure.WebJobs;
  using Moq;
  using NUnit.Framework;
  using QueueClient.Messages;
  using QueueProcessor.Jobs;
  using QueueProcessor.Tracing;
  using RealArtists.ShipHub.Actors;

  [TestFixture]
  [AutoRollback]
  public class WebhookHandlerTests {
    private static IShipHubConfiguration Configuration { get; } = new ShipHubCloudConfiguration();

    public static RepositoryActor CreateRepoActor(long repoId, string fullName) {
      var repoActor = new RepositoryActor(null, null, null, null, Configuration);
      repoActor.Initialize(repoId, fullName);
      return repoActor;
    }

    public static OrganizationActor CreateOrgActor(long orgId, string login) {
      var orgActor = new OrganizationActor(null, null, null, null, Configuration);
      orgActor.Initialize(orgId, login);
      return orgActor;
    }

    [Test]
    public async Task WillEditHookWhenEventListIsNotCompleteForRepo() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = context.Hooks.Add(new Hook() {
          Id = 1001,
          Events = "event1,event2",
          GitHubId = 8001,
          RepositoryId = repo.Id,
          Secret = Guid.NewGuid(),
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.RepositoryWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
              new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = false,
                  Secret = "*******",
                  Url = $"https://{Configuration.ApiHostName}/webhook/repo/1234",
                },
                Events = new string[] {
                  "event1",
                  "event2",
                },
                Name = "web",
              },
            },
          });

        mock
          .Setup(x => x.EditRepositoryWebhookEvents(repo.FullName, (long)hook.GitHubId, It.IsAny<IEnumerable<string>>()))
          .Returns((string repoName, long hookId, IEnumerable<string> eventList) => {
            var result = new GitHubResponse<Webhook>(null) {
              Result = new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = false,
                  Secret = "*******",
                  Url = $"https://{Configuration.ApiHostName}/webhook/repo/1234",
                },
                Events = eventList,
                Name = "web",
              },
              Status = HttpStatusCode.OK,
            };
            return Task.FromResult(result);
          });

        var repoActor = CreateRepoActor(repo.Id, repo.FullName);
        var changes = await repoActor.AddOrUpdateRepositoryWebhooks(context, mock.Object);

        mock.Verify(x => x.EditRepositoryWebhookEvents(repo.FullName, (long)hook.GitHubId, RepositoryActor.RequiredEvents));
        context.Entry(hook).Reload();
        Assert.IsTrue(RepositoryActor.RequiredEvents.SetEquals(hook.Events.Split(',')));
        Assert.IsFalse(changes.Repositories.Any());
      }
    }

    [Test]
    public async Task WillEditHookWhenEventListIsExcessiveForRepo() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var extraEvents = RepositoryActor.RequiredEvents.Add("extra");
        var extraEventsString = string.Join(",", extraEvents);
        var hook = context.Hooks.Add(new Hook() {
          Id = 1001,
          Events = extraEventsString,
          GitHubId = 8001,
          RepositoryId = repo.Id,
          Secret = Guid.NewGuid(),
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.RepositoryWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
              new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = false,
                  Secret = "*******",
                  Url = $"https://{Configuration.ApiHostName}/webhook/repo/1234",
                },
                Events = extraEvents,
                Name = "web",
              },
            },
          });

        mock
          .Setup(x => x.EditRepositoryWebhookEvents(repo.FullName, (long)hook.GitHubId, It.IsAny<IEnumerable<string>>()))
          .Returns((string repoName, long hookId, IEnumerable<string> eventList) => {
            var result = new GitHubResponse<Webhook>(null) {
              Result = new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = false,
                  Secret = "*******",
                  Url = $"https://{Configuration.ApiHostName}/webhook/repo/1234",
                },
                Events = eventList,
                Name = "web",
              },
              Status = HttpStatusCode.OK,
            };
            return Task.FromResult(result);
          });

        var repoActor = CreateRepoActor(repo.Id, repo.FullName);
        var changes = await repoActor.AddOrUpdateRepositoryWebhooks(context, mock.Object);

        mock.Verify(x => x.EditRepositoryWebhookEvents(repo.FullName, (long)hook.GitHubId, RepositoryActor.RequiredEvents));
        context.Entry(hook).Reload();
        Assert.IsTrue(RepositoryActor.RequiredEvents.SetEquals(hook.Events.Split(',')));
        Assert.IsFalse(changes.Repositories.Any());
      }
    }

    /// <summary>
    /// To guard against webhooks accumulating on the GitHub side, we'll
    /// always remove any existing webhooks that point back to our host before
    /// we add a new one.
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task WillRemoveExistingHooksBeforeAddingOneForRepo() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.RepositoryWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
                  new Webhook() {
                    Id = 8001,
                    Active = true,
                    Config = new WebhookConfiguration() {
                      ContentType = "json",
                      InsecureSsl = false,
                      Secret = "*******",
                      Url = $"https://{Configuration.ApiHostName}/webhook/repo/1",
                    },
                    Events = new string[] {
                    },
                    Name = "web",
                  },
                  new Webhook() {
                    Id = 8002,
                    Active = true,
                    Config = new WebhookConfiguration() {
                      ContentType = "json",
                      InsecureSsl = false,
                      Secret = "*******",
                      Url = $"https://{Configuration.ApiHostName}/webhook/repo/2",
                    },
                    Events = new string[] {
                    },
                    Name = "web",
                  },
            },
            Status = HttpStatusCode.OK,
          });

        var deletedHookIds = new List<long>();

        mock
          .Setup(x => x.DeleteRepositoryWebhook(repo.FullName, It.IsAny<long>()))
          .ReturnsAsync(new GitHubResponse<bool>(null) {
            Result = true,
            Status = HttpStatusCode.OK,
          })
          .Callback((string fullName, long hookId) => {
            deletedHookIds.Add(hookId);
          });

        mock
          .Setup(x => x.AddRepositoryWebhook(repo.FullName, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            },
            Status = HttpStatusCode.OK,
          });

        var repoActor = CreateRepoActor(repo.Id, repo.FullName);
        var changes = await repoActor.AddOrUpdateRepositoryWebhooks(context, mock.Object);

        var hook = context.Hooks.Single(x => x.RepositoryId == repo.Id);

        Assert.AreEqual(new long[] { 8001, 8002 }, deletedHookIds.ToArray());
        Assert.NotNull(hook);
      }
    }

    [Test]
    public async Task WillAddHookWhenNoneExistsForRepo() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        await context.SaveChangesAsync();

        var repoLogItem = context.SyncLogs.Single(x => x.OwnerType == "repo" && x.OwnerId == repo.Id && x.ItemType == "repository" && x.ItemId == repo.Id);
        var repoLogItemRowVersion = repoLogItem.RowVersion;

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.RepositoryWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
            Status = HttpStatusCode.OK,
          });

        string installRepoName = null;
        Webhook installWebhook = null;

        mock
          .Setup(x => x.AddRepositoryWebhook(repo.FullName, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            },
            Status = HttpStatusCode.OK,
          })
          .Callback((string fullName, Webhook webhook) => {
            installRepoName = fullName;
            installWebhook = webhook;
          });

        var repoActor = CreateRepoActor(repo.Id, repo.FullName);
        var changes = await repoActor.AddOrUpdateRepositoryWebhooks(context, mock.Object);

        var hook = context.Hooks.Single(x => x.RepositoryId == repo.Id);

        Assert.IsTrue(RepositoryActor.RequiredEvents.SetEquals(hook.Events.Split(',')));
        Assert.AreEqual(repo.Id, hook.RepositoryId);
        Assert.AreEqual(9999, hook.GitHubId);
        Assert.Null(hook.OrganizationId);
        Assert.Null(hook.LastSeen);
        Assert.NotNull(hook.Secret);

        Assert.AreEqual(repo.FullName, installRepoName);
        Assert.AreEqual("web", installWebhook.Name);
        Assert.AreEqual(true, installWebhook.Active);
        Assert.IsTrue(RepositoryActor.RequiredEvents.SetEquals(installWebhook.Events));
        Assert.AreEqual("json", installWebhook.Config.ContentType);
        Assert.AreEqual(false, installWebhook.Config.InsecureSsl);
        Assert.AreEqual(hook.Secret.ToString(), installWebhook.Config.Secret);

        repoLogItem = context.SyncLogs.Single(x => x.OwnerType == "repo" && x.OwnerId == repo.Id && x.ItemType == "repository" && x.ItemId == repo.Id);
        Assert.Greater(repoLogItem.RowVersion, repoLogItemRowVersion,
          "row version should get bumped so the repo gets synced");
        Assert.AreEqual(new long[] { repo.Id }, changes.Repositories.ToArray());
      }
    }

    [Test]
    public async Task RepoHookSetLastErrorIfGitHubAddRequestFails() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.RepositoryWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
            Status = HttpStatusCode.OK,
          });
        mock
           .Setup(x => x.AddRepositoryWebhook(repo.FullName, It.IsAny<Webhook>()))
           .ThrowsAsync(new Exception("some exception!"));

        var repoActor = CreateRepoActor(repo.Id, repo.FullName);
        var changes = await repoActor.AddOrUpdateRepositoryWebhooks(context, mock.Object);

        Assert.IsEmpty(changes.Repositories, "Failed hook creation should not send notifications.");

        var hook = context.Hooks.SingleOrDefault(x => x.RepositoryId == repo.Id);
        Assert.IsNotNull(hook.LastError, "hook should have been marked as errored when we noticed the AddRepoHook failed");
      }
    }

    [Test]
    public async Task OrgHookIsRemovedIfGitHubAddRequestFails() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        context.OrganizationAccounts.Add(new OrganizationAccount() {
          UserId = user.Id,
          OrganizationId = org.Id,
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.OrganizationWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
            Status = HttpStatusCode.OK,
          });
        mock
           .Setup(x => x.AddOrganizationWebhook(org.Login, It.IsAny<Webhook>()))
           .ThrowsAsync(new Exception("some exception!"));

        bool exceptionThrown = false;
        try {
          var orgActor = CreateOrgActor(org.Id, org.Login);
          await orgActor.AddOrUpdateOrganizationWebhooks(context, mock.Object);
        } catch {
          exceptionThrown = true;
        }
        Assert.True(exceptionThrown, "Creating hook should throw exception.");

        var hook = context.Hooks.SingleOrDefault(x => x.OrganizationId == org.Id);
        Assert.IsNull(hook, "hook should have been removed when we noticed the AddRepoHook failed");
      }
    }

    [Test]
    public async Task WillAddHookWhenNoneExistsForOrg() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();
        await context.SetUserOrganizations(user.Id, new[] { org.Id });

        var orgLogItem = context.SyncLogs.Single(x => x.OwnerType == "org" && x.OwnerId == org.Id && x.ItemType == "account" && x.ItemId == org.Id);
        var orgLogItemRowVersion = orgLogItem.RowVersion;

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.OrganizationWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
            Status = HttpStatusCode.OK,
          });

        Webhook installWebhook = null;

        mock
          .Setup(x => x.AddOrganizationWebhook(org.Login, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            },
            Status = HttpStatusCode.OK,
          })
          .Callback((string login, Webhook webhook) => {
            installWebhook = webhook;
          });

        var orgActor = CreateOrgActor(org.Id, org.Login);
        var changes = await orgActor.AddOrUpdateOrganizationWebhooks(context, mock.Object);
        var hook = context.Hooks.Single(x => x.OrganizationId == org.Id);

        Assert.AreEqual(OrganizationActor.RequiredEvents, new HashSet<string>(hook.Events.Split(',')));
        Assert.AreEqual(org.Id, hook.OrganizationId);
        Assert.AreEqual(9999, hook.GitHubId);
        Assert.Null(hook.RepositoryId);
        Assert.Null(hook.LastSeen);
        Assert.NotNull(hook.Secret);

        Assert.AreEqual("web", installWebhook.Name);
        Assert.AreEqual(true, installWebhook.Active);
        Assert.AreEqual(OrganizationActor.RequiredEvents, new HashSet<string>(installWebhook.Events));
        Assert.AreEqual("json", installWebhook.Config.ContentType);
        Assert.AreEqual(false, installWebhook.Config.InsecureSsl);
        Assert.AreEqual(hook.Secret.ToString(), installWebhook.Config.Secret);

        orgLogItem = context.SyncLogs.Single(x => x.OwnerType == "org" && x.OwnerId == org.Id && x.ItemType == "account" && x.ItemId == org.Id);
        Assert.Greater(orgLogItem.RowVersion, orgLogItemRowVersion,
          "row version should get bumped so the org gets synced");
        Assert.AreEqual(new long[] { org.Id }, changes.Organizations.ToArray());
      }
    }

    /// <summary>
    /// To guard against webhooks accumulating on the GitHub side, we'll
    /// always remove any existing webhooks that point back to our host before
    /// we add a new one.
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task WillRemoveExistingHooksBeforeAddingOneForOrg() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.OrganizationWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Status = HttpStatusCode.OK,
            Result = new List<Webhook>() {
                  new Webhook() {
                    Id = 8001,
                    Active = true,
                    Config = new WebhookConfiguration() {
                      ContentType = "json",
                      InsecureSsl = false,
                      Secret = "*******",
                      Url = $"https://{Configuration.ApiHostName}/webhook/org/1",
                    },
                    Events = new string[] {
                    },
                    Name = "web",
                  },
                  new Webhook() {
                    Id = 8002,
                    Active = true,
                    Config = new WebhookConfiguration() {
                      ContentType = "json",
                      InsecureSsl = false,
                      Secret = "*******",
                      Url = $"https://{Configuration.ApiHostName}/webhook/repo/2",
                    },
                    Events = new string[] {
                    },
                    Name = "web",
                  },
            },
          });

        var deletedHookIds = new List<long>();

        mock
          .Setup(x => x.DeleteOrganizationWebhook(org.Login, It.IsAny<long>()))
          .ReturnsAsync(new GitHubResponse<bool>(null) {
            Result = true,
            Status = HttpStatusCode.OK,
          })
          .Callback((string fullName, long hookId) => {
            deletedHookIds.Add(hookId);
          });

        mock
          .Setup(x => x.AddOrganizationWebhook(org.Login, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            },
            Status = HttpStatusCode.OK,
          });

        var orgActor = CreateOrgActor(org.Id, org.Login);
        await orgActor.AddOrUpdateOrganizationWebhooks(context, mock.Object);
        var hook = context.Hooks.Single(x => x.OrganizationId == org.Id);

        Assert.AreEqual(new long[] { 8001, 8002 }, deletedHookIds.ToArray());
        Assert.NotNull(hook);
      }
    }

    [Test]
    public async Task WillEditHookWhenEventListIsNotCompleteForOrg() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        context.OrganizationAccounts.Add(new OrganizationAccount() {
          UserId = user.Id,
          OrganizationId = org.Id,
        });
        var hook = context.Hooks.Add(new Hook() {
          Id = 1001,
          Events = "event1,event2",
          GitHubId = 8001,
          OrganizationId = org.Id,
          Secret = Guid.NewGuid(),
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.OrganizationWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
              new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = false,
                  Secret = "*******",
                  Url = $"https://{Configuration.ApiHostName}/webhook/repo/1234",
                },
                Events = new string[] {
                  "event1",
                  "event2",
                },
                Name = "web",
              },
            },
            Status = HttpStatusCode.OK,
          });

        mock
          .Setup(x => x.EditOrganizationWebhookEvents(org.Login, (long)hook.GitHubId, It.IsAny<IEnumerable<string>>()))
          .Returns((string repoName, long hookId, IEnumerable<string> eventList) => {
            var result = new GitHubResponse<Webhook>(null) {
              Result = new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = false,
                  Secret = "*******",
                  Url = $"https://{Configuration.ApiHostName}/webhook/org/1234",
                },
                Events = eventList,
                Name = "web",
              },
              Status = HttpStatusCode.OK,
            };
            return Task.FromResult(result);
          });

        var orgActor = CreateOrgActor(org.Id, org.Login);
        await orgActor.AddOrUpdateOrganizationWebhooks(context, mock.Object);

        mock.Verify(x => x.EditOrganizationWebhookEvents(org.Login, (long)hook.GitHubId, OrganizationActor.RequiredEvents));
        context.Entry(hook).Reload();
        Assert.AreEqual(OrganizationActor.RequiredEvents.ToArray(), hook.Events.Split(','));
      }
    }

    [Test]
    public async Task WillEditHookWhenEventListIsExcessiveForOrg() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        context.OrganizationAccounts.Add(new OrganizationAccount() {
          UserId = user.Id,
          OrganizationId = org.Id,
        });
        var extraEvents = OrganizationActor.RequiredEvents.Add("extra");
        var extraEventsString = string.Join(",", extraEvents);
        var hook = context.Hooks.Add(new Hook() {
          Id = 1001,
          Events = extraEventsString,
          GitHubId = 8001,
          OrganizationId = org.Id,
          Secret = Guid.NewGuid(),
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.OrganizationWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
              new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = false,
                  Secret = "*******",
                  Url = $"https://{Configuration.ApiHostName}/webhook/repo/1234",
                },
                Events = extraEvents,
                Name = "web",
              },
            },
            Status = HttpStatusCode.OK,
          });

        mock
          .Setup(x => x.EditOrganizationWebhookEvents(org.Login, (long)hook.GitHubId, It.IsAny<IEnumerable<string>>()))
          .Returns((string repoName, long hookId, IEnumerable<string> eventList) => {
            var result = new GitHubResponse<Webhook>(null) {
              Result = new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = false,
                  Secret = "*******",
                  Url = $"https://{Configuration.ApiHostName}/webhook/org/1234",
                },
                Events = eventList,
                Name = "web",
              },
              Status = HttpStatusCode.OK,
            };
            return Task.FromResult(result);
          });

        var orgActor = CreateOrgActor(org.Id, org.Login);
        await orgActor.AddOrUpdateOrganizationWebhooks(context, mock.Object);

        mock.Verify(x => x.EditOrganizationWebhookEvents(org.Login, (long)hook.GitHubId, OrganizationActor.RequiredEvents));
        context.Entry(hook).Reload();
        Assert.AreEqual(OrganizationActor.RequiredEvents.ToArray(), hook.Events.Split(','));
      }
    }

    [Test]
    public async Task OrgHookWithNullGitHubIdIsRemoved() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        context.OrganizationAccounts.Add(new OrganizationAccount() {
          UserId = user.Id,
          OrganizationId = org.Id,
        });
        var hook = context.Hooks.Add(new Hook() {
          Id = 1001,
          Events = "event1,event2",
          GitHubId = null, // Empty GitHub Id
          OrganizationId = org.Id,
          Secret = Guid.NewGuid(),
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.OrganizationWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
            },
            Status = HttpStatusCode.OK,
          });
        mock
          .Setup(x => x.AddOrganizationWebhook(org.Login, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            },
            Status = HttpStatusCode.OK,
          });

        var orgActor = CreateOrgActor(org.Id, org.Login);
        await orgActor.AddOrUpdateOrganizationWebhooks(context, mock.Object);

        var oldHook = context.Hooks.SingleOrDefault(x => x.Id == 1001);
        Assert.Null(oldHook, "should have been deleted because it had a null GitHubId");

        var newHook = context.Hooks.SingleOrDefault(x => x.OrganizationId == org.Id);
        Assert.NotNull(newHook);
        Assert.AreEqual(9999, newHook.GitHubId);
      }
    }

    [Test]
    public async Task RepoHookWithErrorIsSkipped() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = context.Hooks.Add(new Hook() {
          Id = 1001,
          Events = "event1,event2",
          GitHubId = null, // Empty GitHubId
          RepositoryId = repo.Id,
          Secret = Guid.NewGuid(),
          LastError = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        var repoActor = CreateRepoActor(repo.Id, repo.FullName);
        var changes = await repoActor.AddOrUpdateRepositoryWebhooks(context, null);

        var beforeError = hook.LastError;
        await context.Entry(hook).ReloadAsync();
        Assert.IsTrue(hook.LastError == beforeError, "Recent LastError should be skipped.");
        Assert.IsEmpty(changes.Repositories, "skipped hook should not send changes.");
      }
    }

    [Test]
    public async Task RepoHookWithOldErrorIsRetried() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = context.Hooks.Add(new Hook() {
          Id = 1001,
          Events = "event1,event2",
          GitHubId = null, // Empty GitHubId
          RepositoryId = repo.Id,
          Secret = Guid.NewGuid(),
          LastError = DateTimeOffset.UtcNow.Subtract(RepositoryActor.HookErrorDelay),
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();
        mock
          .Setup(x => x.RepositoryWebhooks(repo.FullName, It.IsAny<GitHubCacheDetails>()))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
            Status = HttpStatusCode.OK,
          });
        mock
          .Setup(x => x.AddRepositoryWebhook(repo.FullName, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            },
            Status = HttpStatusCode.OK,
          });

        var repoActor = CreateRepoActor(repo.Id, repo.FullName);
        var changes = await repoActor.AddOrUpdateRepositoryWebhooks(context, mock.Object);

        await context.Entry(hook).ReloadAsync();
        Assert.AreEqual(9999, hook.GitHubId);
        Assert.IsNull(hook.LastError);
        Assert.IsTrue(changes.Repositories.First() == repo.Id, "New hook should send notifications.");
      }
    }
  }
}
