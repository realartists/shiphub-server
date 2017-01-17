﻿namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
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

  [TestFixture]
  [AutoRollback]
  public class WebhookHandlerTests {
    private static IShipHubConfiguration Configuration { get; } = new ShipHubCloudConfiguration();

    public static WebhookQueueHandler CreateHandler() {
      return new WebhookQueueHandler(Configuration, null, new DetailedExceptionLogger());
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

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await CreateHandler().AddOrUpdateRepoWebhooksWithClient(new TargetMessage(repo.Id, user.Id), mock.Object, collectorMock.Object);

        mock.Verify(x => x.EditRepositoryWebhookEvents(repo.FullName, (long)hook.GitHubId, WebhookQueueHandler.RequiredEvents));
        context.Entry(hook).Reload();
        Assert.AreEqual(WebhookQueueHandler.RequiredEvents, hook.Events.Split(',').ToHashSet());
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
          .Setup(x => x.RepositoryWebhooks(repo.FullName, GitHubCacheDetails.Empty))
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

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await CreateHandler().AddOrUpdateRepoWebhooksWithClient(new TargetMessage(repo.Id, user.Id), mock.Object, collectorMock.Object);
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
          .Setup(x => x.RepositoryWebhooks(repo.FullName, GitHubCacheDetails.Empty))
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

        var changeMessages = new List<ChangeMessage>();

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        await CreateHandler().AddOrUpdateRepoWebhooksWithClient(new TargetMessage(repo.Id, user.Id), mock.Object, collectorMock.Object);
        var hook = context.Hooks.Single(x => x.RepositoryId == repo.Id);

        Assert.AreEqual(WebhookQueueHandler.RequiredEvents, new HashSet<string>(hook.Events.Split(',')));
        Assert.AreEqual(repo.Id, hook.RepositoryId);
        Assert.AreEqual(9999, hook.GitHubId);
        Assert.Null(hook.OrganizationId);
        Assert.Null(hook.LastSeen);
        Assert.NotNull(hook.Secret);

        Assert.AreEqual(repo.FullName, installRepoName);
        Assert.AreEqual("web", installWebhook.Name);
        Assert.AreEqual(true, installWebhook.Active);
        Assert.AreEqual(WebhookQueueHandler.RequiredEvents, new HashSet<string>(installWebhook.Events));
        Assert.AreEqual("json", installWebhook.Config.ContentType);
        Assert.AreEqual(false, installWebhook.Config.InsecureSsl);
        Assert.AreEqual(hook.Secret.ToString(), installWebhook.Config.Secret);

        repoLogItem = context.SyncLogs.Single(x => x.OwnerType == "repo" && x.OwnerId == repo.Id && x.ItemType == "repository" && x.ItemId == repo.Id);
        Assert.Greater(repoLogItem.RowVersion, repoLogItemRowVersion,
          "row version should get bumped so the repo gets synced");
        Assert.AreEqual(new long[] { repo.Id }, changeMessages[0].Repositories.ToArray());
      }
    }

    [Test]
    public async Task RepoHookIsRemovedIfGitHubAddRequestFails() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.RepositoryWebhooks(repo.FullName, GitHubCacheDetails.Empty))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
            Status = HttpStatusCode.OK,
          });
        mock
           .Setup(x => x.AddRepositoryWebhook(repo.FullName, It.IsAny<Webhook>()))
           .ThrowsAsync(new Exception("some exception!"));

        bool exceptionThrown = false;
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        try {
          await CreateHandler().AddOrUpdateRepoWebhooksWithClient(new TargetMessage(repo.Id, user.Id), mock.Object, collectorMock.Object);
        } catch {
          exceptionThrown = true;
        }
        Assert.True(exceptionThrown, "Creating hook should throw exception.");

        var hook = context.Hooks.SingleOrDefault(x => x.RepositoryId == repo.Id);
        Assert.IsNull(hook, "hook should have been removed when we noticed the AddRepoHook failed");
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
          .Setup(x => x.OrganizationWebhooks(org.Login, GitHubCacheDetails.Empty))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
            Status = HttpStatusCode.OK,
          });
        mock
           .Setup(x => x.AddOrganizationWebhook(org.Login, It.IsAny<Webhook>()))
           .ThrowsAsync(new Exception("some exception!"));

        bool exceptionThrown = false;
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        try {
          await CreateHandler().AddOrUpdateOrgWebhooksWithClient(new TargetMessage(org.Id, user.Id), mock.Object, collectorMock.Object);
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
          .Setup(x => x.OrganizationWebhooks(org.Login, GitHubCacheDetails.Empty))
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

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        await CreateHandler().AddOrUpdateOrgWebhooksWithClient(new TargetMessage(org.Id, user.Id), mock.Object, collectorMock.Object);
        var hook = context.Hooks.Single(x => x.OrganizationId == org.Id);

        var expectedEvents = new string[] {
          "repository",
        };

        Assert.AreEqual(new HashSet<string>(expectedEvents), new HashSet<string>(hook.Events.Split(',')));
        Assert.AreEqual(org.Id, hook.OrganizationId);
        Assert.AreEqual(9999, hook.GitHubId);
        Assert.Null(hook.RepositoryId);
        Assert.Null(hook.LastSeen);
        Assert.NotNull(hook.Secret);

        Assert.AreEqual("web", installWebhook.Name);
        Assert.AreEqual(true, installWebhook.Active);
        Assert.AreEqual(new HashSet<string>(expectedEvents), new HashSet<string>(installWebhook.Events));
        Assert.AreEqual("json", installWebhook.Config.ContentType);
        Assert.AreEqual(false, installWebhook.Config.InsecureSsl);
        Assert.AreEqual(hook.Secret.ToString(), installWebhook.Config.Secret);

        orgLogItem = context.SyncLogs.Single(x => x.OwnerType == "org" && x.OwnerId == org.Id && x.ItemType == "account" && x.ItemId == org.Id);
        Assert.Greater(orgLogItem.RowVersion, orgLogItemRowVersion,
          "row version should get bumped so the org gets synced");
        Assert.AreEqual(new long[] { org.Id }, changeMessages[0].Organizations.ToArray());
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
        context.OrganizationAccounts.Add(new OrganizationAccount() {
          UserId = user.Id,
          OrganizationId = org.Id,
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();

        mock
          .Setup(x => x.OrganizationWebhooks(org.Login, GitHubCacheDetails.Empty))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
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

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await CreateHandler().AddOrUpdateOrgWebhooksWithClient(new TargetMessage(org.Id, user.Id), mock.Object, collectorMock.Object);
        var hook = context.Hooks.Single(x => x.OrganizationId == org.Id);

        Assert.AreEqual(new long[] { 8001, 8002 }, deletedHookIds.ToArray());
        Assert.NotNull(hook);
      }
    }

    [Test]
    public async Task WillEditHookWhenEventListIsNotCompleteForOrg() {
      var expectedEvents = new string[] {
          "repository",
        };

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
          .Setup(x => x.EditOrganizationWebhookEvents(org.Login, (long)hook.GitHubId, It.IsAny<string[]>()))
          .Returns((string repoName, long hookId, string[] eventList) => {
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

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await CreateHandler().AddOrUpdateOrgWebhooksWithClient(new TargetMessage(org.Id, user.Id), mock.Object, collectorMock.Object);

        mock.Verify(x => x.EditOrganizationWebhookEvents(org.Login, (long)hook.GitHubId, expectedEvents));
        context.Entry(hook).Reload();
        Assert.AreEqual(expectedEvents, hook.Events.Split(','));
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
          .Setup(x => x.OrganizationWebhooks(org.Login, GitHubCacheDetails.Empty))
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

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await CreateHandler().AddOrUpdateOrgWebhooksWithClient(new TargetMessage(org.Id, user.Id), mock.Object, collectorMock.Object);

        var oldHook = context.Hooks.SingleOrDefault(x => x.Id == 1001);
        Assert.Null(oldHook, "should have been deleted because it had a null GitHubId");

        var newHook = context.Hooks.SingleOrDefault(x => x.OrganizationId == org.Id);
        Assert.NotNull(newHook);
        Assert.AreEqual(9999, newHook.GitHubId);
      }
    }

    [Test]
    public async Task RepoHookWithNullGitHubIdIsRemoved() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = context.Hooks.Add(new Hook() {
          Id = 1001,
          Events = "event1,event2",
          GitHubId = null, // Empty GitHubId
          RepositoryId = repo.Id,
          Secret = Guid.NewGuid(),
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubActor>();
        mock
          .Setup(x => x.RepositoryWebhooks(repo.FullName, GitHubCacheDetails.Empty))
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

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await CreateHandler().AddOrUpdateRepoWebhooksWithClient(new TargetMessage(repo.Id, user.Id), mock.Object, collectorMock.Object);

        var oldHook = context.Hooks.SingleOrDefault(x => x.Id == 1001);
        Assert.Null(oldHook, "should have been deleted because it had a null GitHubId");

        var newHook = context.Hooks.SingleOrDefault(x => x.RepositoryId == repo.Id);
        Assert.NotNull(newHook);
        Assert.AreEqual(9999, newHook.GitHubId);
      }
    }
  }
}
