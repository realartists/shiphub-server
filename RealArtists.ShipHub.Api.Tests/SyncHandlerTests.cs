namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Microsoft.Azure;
  using Moq;
  using RealArtists.ShipHub.Common.DataModel;
  using RealArtists.ShipHub.Common.GitHub;
  using RealArtists.ShipHub.Common.GitHub.Models;
  using RealArtists.ShipHub.QueueClient.Messages;
  using RealArtists.ShipHub.QueueProcessor;
  using Xunit;

  public class SyncHandlerTests {

    static string ApiHostname {
      get {
        var hostname = CloudConfigurationManager.GetSetting("ApiHostname");
        Assert.NotNull(hostname);
        return hostname;
      }
    }

    [Fact]
    [AutoRollback]
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

      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = context.Hooks.Add(new Hook() {
          Id = 1001,
          Active = true,
          Events = "event1,event2",
          GitHubId = 8001,
          RepositoryId = repo.Id,
          Secret = Guid.NewGuid(),
        });
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.RepoWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>() {
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
          .Setup(x => x.EditWebhookEvents(repo.FullName, hook.GitHubId, It.IsAny<string[]>(), null))
          .Returns((string repoName, long hookId, string[] eventList, IGitHubCacheOptions opts) => {
            var result = new GitHubResponse<Webhook>() {
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

        await SyncHandler.AddOrUpdateRepoWebhooksWithClient(new AddOrUpdateRepoWebhooksMessage() {
          RepositoryId = repo.Id,
          AccessToken = user.Token,
        }, mock.Object);

        context.Entry(hook).Reload();

        Assert.Equal(expectedEvents, hook.Events.Split(','));
      }
    }

    /// <summary>
    /// To guard against webhooks accumulating on the GitHub side, we'll
    /// always remove any existing webhooks that point back to our host before
    /// we add a new one.
    /// </summary>
    /// <returns></returns>
    [Fact]
    [AutoRollback]
    public async Task WillRemoveExistingHooksBeforeAddingOneForRepo() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.RepoWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>() {
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
          .Setup(x => x.DeleteWebhook(repo.FullName, It.IsAny<long>(), null))
          .ReturnsAsync(new GitHubResponse<bool>() {
            Result = true,
          })
          .Callback((string fullName, long hookId, IGitHubCacheOptions opts) => {
            deletedHookIds.Add(hookId);
          });

        mock
          .Setup(x => x.AddRepoWebhook(repo.FullName, It.IsAny<Webhook>(), null))
          .ReturnsAsync(new GitHubResponse<Webhook>() {
            Result = new Webhook() {
              Id = 9999,
            }
          });

        await SyncHandler.AddOrUpdateRepoWebhooksWithClient(new AddOrUpdateRepoWebhooksMessage() {
          RepositoryId = repo.Id,
          AccessToken = user.Token,
        }, mock.Object);
        var hook = context.Hooks.Single(x => x.RepositoryId == repo.Id);

        Assert.Equal(new long[] { 8001, 8002 }, deletedHookIds.ToArray());
        Assert.NotNull(hook);
      }
    }

    [Fact]
    [AutoRollback]
    public async Task WillAddHookWhenNoneExistsForRepo() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        await context.SaveChangesAsync();

        var mock = new Mock<IGitHubClient>();

        mock
          .Setup(x => x.RepoWebhooks(repo.FullName, null))
          .ReturnsAsync(new GitHubResponse<IEnumerable<Webhook>>() {
            Result = new List<Webhook>(),
          });

        string installRepoName = null;
        Webhook installWebHook = null;

        mock
          .Setup(x => x.AddRepoWebhook(repo.FullName, It.IsAny<Webhook>(), null))
          .ReturnsAsync(new GitHubResponse<Webhook>() {
            Result = new Webhook() {
              Id = 9999,
            }
          })
          .Callback((string fullName, Webhook webhook, IGitHubCacheOptions opts) => {
            installRepoName = fullName;
            installWebHook = webhook;
          });

        await SyncHandler.AddOrUpdateRepoWebhooksWithClient(new AddOrUpdateRepoWebhooksMessage() {
          RepositoryId = repo.Id,
          AccessToken = user.Token,
        }, mock.Object);
        var hook = context.Hooks.Single(x => x.RepositoryId == repo.Id);

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

        Assert.Equal(new HashSet<string>(expectedEvents), new HashSet<string>(hook.Events.Split(',')));
        Assert.Equal(repo.Id, hook.RepositoryId);
        Assert.Equal(9999, hook.GitHubId);
        Assert.Null(hook.OrganizationId);
        Assert.Null(hook.LastSeen);
        Assert.NotNull(hook.Secret);

        Assert.Equal(repo.FullName, installRepoName);
        Assert.Equal("web", installWebHook.Name);
        Assert.Equal(true, installWebHook.Active);
        Assert.Equal(new HashSet<string>(expectedEvents), new HashSet<string>(installWebHook.Events));
        Assert.Equal("json", installWebHook.Config.ContentType);
        Assert.Equal(0, installWebHook.Config.InsecureSsl);
        Assert.Equal(hook.Secret.ToString(), installWebHook.Config.Secret);
      }
    }
  }
}

