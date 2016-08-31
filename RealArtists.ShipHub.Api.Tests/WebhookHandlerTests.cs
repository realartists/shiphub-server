namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;
  using Common.DataModel;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Microsoft.Azure;
  using Microsoft.Azure.WebJobs;
  using Moq;
  using NUnit.Framework;
  using QueueClient.Messages;
  using QueueProcessor;

  [TestFixture]
  [AutoRollback]
  public class WebhookHandlerTests {

    static string ApiHostname {
      get {
        var hostname = CloudConfigurationManager.GetSetting("ApiHostname");
        Assert.NotNull(hostname);
        return hostname;
      }
    }

    [Test]
    public async Task WillEditHookWhenEventListIsNotCompleteForRepo() {
      var expectedEvents = new string[] {
          "issues",
          "issue_comment",
          "member",
          "public",
          "pull_request",
          "pull_request_review_comment",
          "repository",
          "team_add",
        };

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

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.RepoWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
              new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = 0,
                  Secret = "*******",
                  Url = $"https://{ApiHostname}/webhook/repo/1234",
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
          .Setup(x => x.EditRepoWebhookEvents(repo.FullName, hook.GitHubId, It.IsAny<string[]>()))
          .Returns((string repoName, long hookId, string[] eventList) => {
            var result = new GitHubResponse<Webhook>(null) {
              Result = new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = 0,
                  Secret = "*******",
                  Url = $"https://{ApiHostname}/webhook/repo/1234",
                },
                Events = new string[] {
                "issues",
                "issue_comment",
                "member",
                "public",
                "pull_request",
                "pull_request_review_comment",
                "repository",
                "team_add",
              },
                Name = "web",
              }
            };
            return Task.FromResult(result);
          });

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await WebhookHandler.AddOrUpdateRepoWebhooksWithClient(new RepoWebhooksMessage() {
          RepositoryId = repo.Id,
          UserId = user.Id,
        }, mock.Object, collectorMock.Object);

        context.Entry(hook).Reload();

        Assert.AreEqual(expectedEvents, hook.Events.Split(','));
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

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.RepoWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
                  new Webhook() {
                    Id = 8001,
                    Active = true,
                    Config = new WebhookConfiguration() {
                      ContentType = "json",
                      InsecureSsl = 0,
                      Secret = "*******",
                      Url = $"https://{ApiHostname}/webhook/repo/1",
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
                      InsecureSsl = 0,
                      Secret = "*******",
                      Url = $"https://{ApiHostname}/webhook/repo/2",
                    },
                    Events = new string[] {
                    },
                    Name = "web",
                  },
            },
          });

        var deletedHookIds = new List<long>();

        mock
          .Setup(x => x.DeleteRepoWebhook(repo.FullName, It.IsAny<long>()))
          .ReturnsAsync(new GitHubResponse<bool>(null) {
            Result = true,
          })
          .Callback((string fullName, long hookId) => {
            deletedHookIds.Add(hookId);
          });

        mock
          .Setup(x => x.AddRepoWebhook(repo.FullName, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            }
          });

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await WebhookHandler.AddOrUpdateRepoWebhooksWithClient(new RepoWebhooksMessage() {
          RepositoryId = repo.Id,
          UserId = user.Id,
        }, mock.Object, collectorMock.Object);
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

        var repoLogItem = context.RepositoryLog.Single(x => x.Type.Equals("repository") && x.ItemId == repo.Id);
        var repoLogItemRowVersion = repoLogItem.RowVersion;

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.RepoWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
          });

        string installRepoName = null;
        Webhook installWebHook = null;

        mock
          .Setup(x => x.AddRepoWebhook(repo.FullName, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            }
          })
          .Callback((string fullName, Webhook webhook) => {
            installRepoName = fullName;
            installWebHook = webhook;
          });

        var changeMessages = new List<ChangeMessage>();

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        await WebhookHandler.AddOrUpdateRepoWebhooksWithClient(new RepoWebhooksMessage() {
          RepositoryId = repo.Id,
          UserId = user.Id,
        }, mock.Object, collectorMock.Object);
        var hook = context.Hooks.Single(x => x.RepositoryId == repo.Id);

        var expectedEvents = new string[] {
          "issues",
          "issue_comment",
          //"member",
          //"public",
          //"pull_request",
          //"pull_request_review_comment",
          //"repository",
          //"team_add",
        };

        Assert.AreEqual(new HashSet<string>(expectedEvents), new HashSet<string>(hook.Events.Split(',')));
        Assert.AreEqual(repo.Id, hook.RepositoryId);
        Assert.AreEqual(9999, hook.GitHubId);
        Assert.Null(hook.OrganizationId);
        Assert.Null(hook.LastSeen);
        Assert.NotNull(hook.Secret);

        Assert.AreEqual(repo.FullName, installRepoName);
        Assert.AreEqual("web", installWebHook.Name);
        Assert.AreEqual(true, installWebHook.Active);
        Assert.AreEqual(new HashSet<string>(expectedEvents), new HashSet<string>(installWebHook.Events));
        Assert.AreEqual("json", installWebHook.Config.ContentType);
        Assert.AreEqual(0, installWebHook.Config.InsecureSsl);
        Assert.AreEqual(hook.Secret.ToString(), installWebHook.Config.Secret);

        context.Entry(repoLogItem).Reload();
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
        
        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.RepoWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
          });
       mock
          .Setup(x => x.AddRepoWebhook(repo.FullName, It.IsAny<Webhook>()))
          .ThrowsAsync(new Exception("some exception!"));
          
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await WebhookHandler.AddOrUpdateRepoWebhooksWithClient(new RepoWebhooksMessage() {
          RepositoryId = repo.Id,
          UserId = user.Id,
        }, mock.Object, collectorMock.Object);

        var hook = context.Hooks.SingleOrDefault(x => x.RepositoryId == repo.Id);
        Assert.IsNull(hook, "hook should have been removed when we noticed the AddRepoHook failed");
      }
    }

    [Test]
    public async Task OrgHookIsRemovedIfGitHubAddRequestFails() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        context.AccountOrganizations.Add(new OrganizationAccount() {
          UserId = user.Id,
          OrganizationId = org.Id,
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.OrgWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
          });
        mock
           .Setup(x => x.AddOrgWebhook(org.Login, It.IsAny<Webhook>()))
           .ThrowsAsync(new Exception("some exception!"));

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await WebhookHandler.AddOrUpdateOrgWebhooksWithClient(new OrgWebhooksMessage() {
          OrganizationId = org.Id,
          UserId = user.Id,
        }, mock.Object, collectorMock.Object);

        var hook = context.Hooks.SingleOrDefault(x => x.OrganizationId == org.Id);
        Assert.IsNull(hook, "hook should have been removed when we noticed the AddRepoHook failed");
      }
    }

    [Test]
    public async Task WillAddHookWhenNoneExistsForOrg() {
      using (var context = new ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = TestUtil.MakeTestOrg(context);
        context.AccountOrganizations.Add(new OrganizationAccount() {
          UserId = user.Id,
          OrganizationId = org.Id,
        });
        await context.SaveChangesAsync();

        var orgLogItem = context.OrganizationLog.Single(x => x.OrganizationId == org.Id && x.AccountId == org.Id);
        var orgLogItemRowVersion = orgLogItem.RowVersion;

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.OrgWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>(),
          });

        Webhook installWebHook = null;

        mock
          .Setup(x => x.AddOrgWebhook(org.Login, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            }
          })
          .Callback((string login, Webhook webhook) => {
            installWebHook = webhook;
          });

        var changeMessages = new List<ChangeMessage>();
        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
          .Returns((ChangeMessage msg, CancellationToken token) => {
            changeMessages.Add(msg);
            return Task.CompletedTask;
          });

        await WebhookHandler.AddOrUpdateOrgWebhooksWithClient(new OrgWebhooksMessage() {
          OrganizationId = org.Id,
          UserId = user.Id,
        }, mock.Object, collectorMock.Object);
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

        Assert.AreEqual("web", installWebHook.Name);
        Assert.AreEqual(true, installWebHook.Active);
        Assert.AreEqual(new HashSet<string>(expectedEvents), new HashSet<string>(installWebHook.Events));
        Assert.AreEqual("json", installWebHook.Config.ContentType);
        Assert.AreEqual(0, installWebHook.Config.InsecureSsl);
        Assert.AreEqual(hook.Secret.ToString(), installWebHook.Config.Secret);

        context.Entry(orgLogItem).Reload();
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
        context.AccountOrganizations.Add(new OrganizationAccount() {
          UserId = user.Id,
          OrganizationId = org.Id,
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.OrgWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
                  new Webhook() {
                    Id = 8001,
                    Active = true,
                    Config = new WebhookConfiguration() {
                      ContentType = "json",
                      InsecureSsl = 0,
                      Secret = "*******",
                      Url = $"https://{ApiHostname}/webhook/org/1",
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
                      InsecureSsl = 0,
                      Secret = "*******",
                      Url = $"https://{ApiHostname}/webhook/repo/2",
                    },
                    Events = new string[] {
                    },
                    Name = "web",
                  },
            },
          });

        var deletedHookIds = new List<long>();

        mock
          .Setup(x => x.DeleteOrgWebhook(org.Login, It.IsAny<long>()))
          .ReturnsAsync(new GitHubResponse<bool>(null) {
            Result = true,
          })
          .Callback((string fullName, long hookId) => {
            deletedHookIds.Add(hookId);
          });

        mock
          .Setup(x => x.AddOrgWebhook(org.Login, It.IsAny<Webhook>()))
          .ReturnsAsync(new GitHubResponse<Webhook>(null) {
            Result = new Webhook() {
              Id = 9999,
            }
          });

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await WebhookHandler.AddOrUpdateOrgWebhooksWithClient(new OrgWebhooksMessage() {
          OrganizationId = org.Id,
          UserId = user.Id,
        }, mock.Object, collectorMock.Object);
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
        context.AccountOrganizations.Add(new OrganizationAccount() {
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

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.OrgWebhooks(org.Login, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>(null) {
            Result = new List<Webhook>() {
              new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = 0,
                  Secret = "*******",
                  Url = $"https://{ApiHostname}/webhook/repo/1234",
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
          .Setup(x => x.EditOrgWebhookEvents(org.Login, hook.GitHubId, It.IsAny<string[]>()))
          .Returns((string repoName, long hookId, string[] eventList) => {
            var result = new GitHubResponse<Webhook>(null) {
              Result = new Webhook() {
                Id = 8001,
                Active = true,
                Config = new WebhookConfiguration() {
                  ContentType = "json",
                  InsecureSsl = 0,
                  Secret = "*******",
                  Url = $"https://{ApiHostname}/webhook/org/1234",
                },
                Events = eventList,
                Name = "web",
              }
            };
            return Task.FromResult(result);
          });

        var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
        await WebhookHandler.AddOrUpdateOrgWebhooksWithClient(new OrgWebhooksMessage() {
          OrganizationId = org.Id,
          UserId = user.Id,
        }, mock.Object, collectorMock.Object);

        context.Entry(hook).Reload();

        Assert.AreEqual(expectedEvents, hook.Events.Split(','));
      }
    }
  }
}
