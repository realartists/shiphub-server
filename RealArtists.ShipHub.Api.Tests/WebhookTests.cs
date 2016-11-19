namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Runtime.Remoting.Metadata.W3cXsd2001;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading.Tasks;
  using System.Web.Http;
  using System.Web.Http.Controllers;
  using System.Web.Http.Hosting;
  using System.Web.Http.Results;
  using System.Web.Http.Routing;
  using ActorInterfaces;
  using AutoMapper;
  using Common;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Controllers;
  using Moq;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
  using NUnit.Framework;
  using Orleans;
  using QueueClient;

  [TestFixture]
  [AutoRollback]
  public class WebhookTests {

    private static string SignatureForPayload(string key, string payload) {
      var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
      byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
      return "sha1=" + new SoapHexBinary(hash).ToString();
    }

    private static IMapper AutoMapper { get; } = CreateMapper();

    private static IMapper CreateMapper() {
      var config = new MapperConfiguration(cfg => {
        cfg.AddProfile<Common.DataModel.GitHubToDataModelProfile>();
        cfg.AddProfile<Api.Sync.Messages.DataModelToApiModelProfile>();

        cfg.CreateMap<Common.DataModel.Milestone, Milestone>(MemberList.Destination);
        cfg.CreateMap<Common.DataModel.Issue, Issue>(MemberList.Destination)
          .ForMember(dest => dest.Reactions, o => o.ResolveUsing(src => src.Reactions == null ? null : src.Reactions.DeserializeObject<ReactionSummary>()))
          .ForMember(dest => dest.PullRequest, o => o.ResolveUsing(src => {
            if (src.PullRequest) {
              return new PullRequestDetails() {
                Url = $"https://api.github.com/repos/{src.Repository.FullName}/pulls/{src.Number}",
              };
            } else {
              return null;
            }
          }));
        cfg.CreateMap<Common.DataModel.Account, Account>(MemberList.Destination)
          .ForMember(x => x.Type, o => o.ResolveUsing(x => x is Common.DataModel.User ? GitHubAccountType.User : GitHubAccountType.Organization));

        cfg.CreateMap<Common.DataModel.Comment, CommentTableType>(MemberList.Destination);
      });

      var mapper = config.CreateMapper();
      return mapper;
    }

    private static void ConfigureController(ApiController controller, string eventName, JObject body, string secretKey) {
      var json = JsonConvert.SerializeObject(body, GitHubSerialization.JsonSerializerSettings);
      var signature = SignatureForPayload(secretKey, json);

      var config = new HttpConfiguration();
      var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/webhook");
      request.Headers.Add("User-Agent", GitHubWebhookController.GitHubUserAgent);
      request.Headers.Add(GitHubWebhookController.EventHeaderName, eventName);
      request.Headers.Add(GitHubWebhookController.SignatureHeaderName, signature);
      request.Headers.Add(GitHubWebhookController.DeliveryIdHeaderName, Guid.NewGuid().ToString());
      request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
      var routeData = new HttpRouteData(config.Routes.MapHttpRoute("Webhook", "webhook"));

      controller.ControllerContext = new HttpControllerContext(config, routeData, request);
      controller.Request = request;
      controller.Request.Properties[HttpPropertyKeys.HttpConfigurationKey] = config;
    }

    private static Task<IChangeSummary> ChangeSummaryFromIssuesHook(JObject obj, string repoOrOrg, long repoOrOrgId, string secret) {
      return ChangeSummaryFromHook("issues", obj, repoOrOrg, repoOrOrgId, secret);
    }

    private static async Task<IChangeSummary> ChangeSummaryFromHook(string eventName, JObject obj, string repoOrOrg, long repoOrOrgId, string secret) {
      IChangeSummary changeSummary = null;

      var mockBusClient = new Mock<IShipHubQueueClient>();
      mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns(Task.CompletedTask)
        .Callback((IChangeSummary arg) => { changeSummary = arg; });

      var controller = new GitHubWebhookController(mockBusClient.Object, AutoMapper, null);
      ConfigureController(controller, eventName, obj, secret);

      IHttpActionResult result = await controller.HandleHook(repoOrOrg, repoOrOrgId);
      Assert.IsInstanceOf(typeof(StatusCodeResult), result);
      Assert.AreEqual(HttpStatusCode.Accepted, (result as StatusCodeResult).StatusCode);

      return changeSummary;
    }

    private static JObject IssueChange(string action, Issue issue, long repositoryId) {
      var obj = new {
        action = action,
        issue = issue,
        repository = new {
          id = repositoryId,
        },
      };
      return JObject.FromObject(obj, GitHubSerialization.JsonSerializer);
    }

    private static Common.DataModel.Organization MakeTestOrg(Common.DataModel.ShipHubContext context) {
      return (Common.DataModel.Organization)context.Accounts.Add(new Common.DataModel.Organization() {
        Id = 6001,
        Login = "myorg",
        Date = DateTimeOffset.UtcNow,
      });
    }

    private static Common.DataModel.Issue MakeTestIssue(Common.DataModel.ShipHubContext context, long accountId, long repoId) {
      var issue = new Common.DataModel.Issue() {
        Id = 1001,
        UserId = accountId,
        RepositoryId = repoId,
        Number = 5,
        State = "open",
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
      };
      context.Issues.Add(issue);
      return issue;
    }

    private Common.DataModel.Hook MakeTestRepoHook(Common.DataModel.ShipHubContext context, long creatorId, long repoId) {
      return context.Hooks.Add(new Common.DataModel.Hook() {
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        RepositoryId = repoId,
      });
    }

    private Common.DataModel.Hook MakeTestOrgHook(Common.DataModel.ShipHubContext context, long creatorId, long orgId) {
      return context.Hooks.Add(new Common.DataModel.Hook() {
        Secret = Guid.NewGuid(),
        Events = "event1,event2",
        OrganizationId = orgId,
      });
    }

    [Test]
    public async Task TestPingSucceedsIfSignatureMatchesRepoHook() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = MakeTestRepoHook(context, user.Id, repo.Id);

        await context.SaveChangesAsync();

        var obj = JObject.FromObject(new {
          hook_id = 1234,
          repository = new {
            id = repo.Id,
          },
        }, GitHubSerialization.JsonSerializer);

        var controller = new GitHubWebhookController(null, AutoMapper, null);
        ConfigureController(controller, "ping", obj, hook.Secret.ToString());
        var result = await controller.HandleHook("repo", repo.Id);
        Assert.IsInstanceOf(typeof(StatusCodeResult), result);
        Assert.AreEqual(HttpStatusCode.Accepted, ((StatusCodeResult)result).StatusCode);
      }
    }

    [Test]
    public async Task TestPingSucceedsIfSignatureMatchesOrgHook() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = MakeTestOrg(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = MakeTestOrgHook(context, user.Id, org.Id);
        context.OrganizationAccounts.Add(new Common.DataModel.OrganizationAccount() {
          UserId = user.Id,
          OrganizationId = org.Id,
        });
        await context.SaveChangesAsync();

        var obj = JObject.FromObject(new {
          hook_id = 1234,
          repository = new {
            id = repo.Id,
          },
          organization = new {
            id = org.Id,
          },
        }, GitHubSerialization.JsonSerializer);

        var controller = new GitHubWebhookController(null, AutoMapper, null);
        ConfigureController(controller, "ping", obj, hook.Secret.ToString());
        var result = await controller.HandleHook("org", org.Id);
        Assert.IsInstanceOf(typeof(StatusCodeResult), result);
        Assert.AreEqual(HttpStatusCode.Accepted, ((StatusCodeResult)result).StatusCode);
      }
    }

    [Test]
    public async Task TestPingFailsWithInvalidSignature() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);

        var hook = context.Hooks.Add(new Common.DataModel.Hook() {
          Secret = Guid.NewGuid(),
          Events = "some events",
          RepositoryId = repo.Id,
        });

        await context.SaveChangesAsync();

        var obj = JObject.FromObject(new {
          hook_id = 1234,
          repository = new {
            id = repo.Id,
          },
        }, GitHubSerialization.JsonSerializer);

        var controller = new GitHubWebhookController(null, AutoMapper, null);
        ConfigureController(controller, "ping", obj, "someIncorrectSignature");
        try {
          await controller.HandleHook("repo", repo.Id);
          Assert.Fail("Should throw exception.");
        } catch (Exception ex) {
          Assert.IsInstanceOf<HttpResponseException>(ex);
        }
      }
    }

    [Test]
    public async Task TestWebhookCallUpdatesLastSeenAndPingCount() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = MakeTestRepoHook(context, user.Id, repo.Id);

        hook.LastSeen = DateTimeOffset.Parse("1/1/2000");
        hook.LastPing = DateTimeOffset.Parse("1/1/2000");
        hook.PingCount = 2;

        await context.SaveChangesAsync();

        var obj = new JObject(
        new JProperty("zen", "It's not fully shipped until it's fast."),
        new JProperty("hook_id", 1234),
        new JProperty("hook", null),
        new JProperty("sender", null),
        new JProperty("repository", new JObject(
          new JProperty("id", repo.Id)
          )));

        var controller = new GitHubWebhookController(null, AutoMapper, null);
        ConfigureController(controller, "ping", obj, hook.Secret.ToString());
        var result = await controller.HandleHook("repo", repo.Id);
        Assert.IsInstanceOf(typeof(StatusCodeResult), result);
        Assert.AreEqual(HttpStatusCode.Accepted, ((StatusCodeResult)result).StatusCode);

        context.Entry(hook).Reload();
        Assert.Greater(hook.LastSeen, DateTimeOffset.Parse("1/1/2000"));
        Assert.IsNull(hook.PingCount);
        Assert.IsNull(hook.LastPing);
      }
    }

    [Test]
    public async Task WillReturnNotFoundWhenCallDoesNotMatchKnownRepoOrOrg() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);

        await context.SaveChangesAsync();

        var obj = JObject.FromObject(new {
          hook_id = 1234,
          repository = new {
            id = repo.Id,
          },
        }, GitHubSerialization.JsonSerializer);

        var controller = new GitHubWebhookController(null, AutoMapper, null);
        ConfigureController(controller, "ping", obj, "someIncorrectSignature");

        Assert.IsInstanceOf<NotFoundResult>(
          await controller.HandleHook("repo", repo.Id),
          "Webhook does not match any known repository or organization."
        );
      }
    }

    [Test]
    public async Task TestIssueMilestoned() {
      Common.DataModel.User testUser;
      Common.DataModel.Repository testRepo;
      Common.DataModel.Issue testIssue;
      Common.DataModel.Hook testHook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        testUser = TestUtil.MakeTestUser(context);
        testRepo = TestUtil.MakeTestRepo(context, testUser.Id);
        testIssue = MakeTestIssue(context, testUser.Id, testRepo.Id);
        testHook = MakeTestRepoHook(context, testUser.Id, testRepo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 5,
        Labels = new List<Label>(),
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
        Milestone = new Milestone() {
          Id = 5001,
          Number = 1234,
          State = "",
          Title = "some milestone",
          Description = "more info about some milestone",
          CreatedAt = DateTimeOffset.Parse("1/1/2016"),
          UpdatedAt = DateTimeOffset.Parse("1/2/2016"),
          DueOn = DateTimeOffset.Parse("2/1/2016"),
          ClosedAt = DateTimeOffset.Parse("3/1/2016"),
          Creator = new Account() {
            Id = testUser.Id,
            Login = testUser.Login,
            Type = GitHubAccountType.User,
          }
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("milestoned", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.NotNull(changeSummary, "should have generated change notification");
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.AreEqual("some milestone", updatedIssue.Milestone.Title);
      }
    }

    [Test]
    public async Task TestIssueDemilestoned() {
      Common.DataModel.User testUser;
      Common.DataModel.Repository testRepo;
      Common.DataModel.Issue testIssue;
      Common.DataModel.Hook testHook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        testUser = TestUtil.MakeTestUser(context);
        testRepo = TestUtil.MakeTestRepo(context, testUser.Id);
        testIssue = MakeTestIssue(context, testUser.Id, testRepo.Id);
        testHook = MakeTestRepoHook(context, testUser.Id, testRepo.Id);
        context.Milestones.Add(new Common.DataModel.Milestone() {
          Id = 5001,
          RepositoryId = testRepo.Id,
          Number = 1234,
          State = "open",
          Title = "some milestone",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        testIssue.MilestoneId = 5001;
        await context.SaveChangesAsync();

        context.Entry(testIssue).Reload();
        Assert.NotNull(testIssue.Milestone);
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 5,
        Labels = new List<Label>(),
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("demilestoned", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.NotNull(changeSummary, "should have generated change notification");
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.Null(updatedIssue.MilestoneId);
      }
    }

    [Test]
    public async Task TestIssueOpened() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 1,
        Labels = new List<Label>(),
        User = new Account() {
          Id = user.Id,
          Login = user.Login,
          Type = GitHubAccountType.User,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("opened", issue, repo.Id), "repo", repo.Id, hook.Secret.ToString());

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var newIssue = context.Issues.First();
        Assert.AreEqual(1001, newIssue.Id);
        Assert.AreEqual(1, newIssue.Number);
        Assert.AreEqual("Some Title", newIssue.Title);
        Assert.AreEqual("Some Body", newIssue.Body);
        Assert.AreEqual("open", newIssue.State);
        Assert.AreEqual(2001, newIssue.RepositoryId);
        Assert.AreEqual(3001, newIssue.UserId);
      }
    }

    [Test]
    public async Task TestIssueClosed() {
      Common.DataModel.User testUser;
      Common.DataModel.Repository testRepo;
      Common.DataModel.Issue testIssue;
      Common.DataModel.Hook testHook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        testUser = TestUtil.MakeTestUser(context);
        testRepo = TestUtil.MakeTestRepo(context, testUser.Id);
        testIssue = MakeTestIssue(context, testUser.Id, testRepo.Id);
        testHook = MakeTestRepoHook(context, testUser.Id, testRepo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "closed",
        Number = 5,
        Labels = new List<Label>(),
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("closed", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.NotNull(changeSummary, "should have generated change notification");
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.AreEqual("closed", updatedIssue.State);
      };
    }

    [Test]
    public async Task TestIssueReopened() {
      Common.DataModel.User testUser;
      Common.DataModel.Repository testRepo;
      Common.DataModel.Issue testIssue;
      Common.DataModel.Hook testHook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        testUser = TestUtil.MakeTestUser(context);
        testRepo = TestUtil.MakeTestRepo(context, testUser.Id);
        testIssue = MakeTestIssue(context, testUser.Id, testRepo.Id);
        testHook = MakeTestRepoHook(context, testUser.Id, testRepo.Id);

        testIssue.State = "closed";

        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 5,
        Labels = new List<Label>(),
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("reopened", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.NotNull(changeSummary, "should have generated change notification");
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.AreEqual("open", updatedIssue.State);
      }
    }

    [Test]
    public async Task TestIssueEdited() {
      Common.DataModel.User testUser;
      Common.DataModel.Repository testRepo;
      Common.DataModel.Issue testIssue;
      Common.DataModel.Hook testHook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        testUser = TestUtil.MakeTestUser(context);
        testRepo = TestUtil.MakeTestRepo(context, testUser.Id);
        testIssue = MakeTestIssue(context, testUser.Id, testRepo.Id);
        testHook = MakeTestRepoHook(context, testUser.Id, testRepo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "A New Title",
        Body = "A New Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 5,
        Labels = new List<Label> {
          new Label() {
            Id = 1,
            Color = "ff0000",
            Name = "Red",
          },
          new Label() {
            Id = 2,
            Color = "0000ff",
            Name = "Blue",
          },
        },
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("edited", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.NotNull(changeSummary, "should have generated change notification");
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.AreEqual("A New Title", updatedIssue.Title);
        Assert.AreEqual("A New Body", updatedIssue.Body);
      };
    }

    [Test]
    public async Task TestIssueAssigned() {
      Common.DataModel.User testUser;
      Common.DataModel.Repository testRepo;
      Common.DataModel.Issue testIssue;
      Common.DataModel.Hook testHook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        testUser = TestUtil.MakeTestUser(context);
        testRepo = TestUtil.MakeTestRepo(context, testUser.Id);
        testIssue = MakeTestIssue(context, testUser.Id, testRepo.Id);
        testHook = MakeTestRepoHook(context, testUser.Id, testRepo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "closed",
        Number = 5,
        Labels = new List<Label>(),
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
        Assignees = new[] {
          new Account() {
            Id = testUser.Id,
            Login = testUser.Login,
            Type = GitHubAccountType.User,
          }
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("assigned", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.AreEqual(testUser.Id, updatedIssue.Assignees.First().Id);
      }
    }

    [Test]
    public async Task TestIssueUnassigned() {
      Common.DataModel.User testUser;
      Common.DataModel.Repository testRepo;
      Common.DataModel.Issue testIssue;
      Common.DataModel.Hook testHook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        testUser = TestUtil.MakeTestUser(context);
        testRepo = TestUtil.MakeTestRepo(context, testUser.Id);
        testIssue = MakeTestIssue(context, testUser.Id, testRepo.Id);
        testHook = MakeTestRepoHook(context, testUser.Id, testRepo.Id);

        testIssue.Assignees = new[] { testUser };

        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "closed",
        Number = 5,
        Labels = new List<Label>(),
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
        Assignees = new Account[0],
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("unassigned", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.AreEqual(0, updatedIssue.Assignees.Count);
      }
    }

    [Test]
    public async Task TestIssueLabeled() {
      Common.DataModel.User testUser;
      Common.DataModel.Repository testRepo;
      Common.DataModel.Issue testIssue;
      Common.DataModel.Hook testHook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        testUser = TestUtil.MakeTestUser(context);
        testRepo = TestUtil.MakeTestRepo(context, testUser.Id);
        testIssue = MakeTestIssue(context, testUser.Id, testRepo.Id);
        testHook = MakeTestRepoHook(context, testUser.Id, testRepo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 5,
        Labels = new List<Label> {
          new Label() {
            Id = 1,
            Color = "ff0000",
            Name = "Red",
          },
          new Label() {
            Id = 2,
            Color = "0000ff",
            Name = "Blue",
          },
        },
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("labeled", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.NotNull(changeSummary, "should have generated change notification");
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        var issueLabels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(2, issueLabels.Count());
        Assert.AreEqual("Blue", issueLabels[0].Name);
        Assert.AreEqual("0000ff", issueLabels[0].Color);
        Assert.AreEqual("Red", issueLabels[1].Name);
        Assert.AreEqual("ff0000", issueLabels[1].Color);

        var updatedRepo = context.Repositories.Single(x => x.Id == testRepo.Id);
        var repoLabels = updatedRepo.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(repoLabels.Select(x => x.Name), issueLabels.Select(x => x.Name),
          "these new labels should be linked with our repo");
      };
    }

    [Test]
    public async Task TestIssueUnlabeled() {
      Common.DataModel.User testUser;
      Common.DataModel.Repository testRepo;
      Common.DataModel.Issue testIssue;
      Common.DataModel.Hook testHook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        testUser = TestUtil.MakeTestUser(context);
        testRepo = TestUtil.MakeTestRepo(context, testUser.Id);
        testIssue = MakeTestIssue(context, testUser.Id, testRepo.Id);
        testHook = MakeTestRepoHook(context, testUser.Id, testRepo.Id);
        await context.SaveChangesAsync();
      }

      // First add the labels Red and Blue
      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 5,
        Labels = new List<Label> {
          new Label() {
            Id = 1,
            Color = "ff0000",
            Name = "Red",
          },
          new Label() {
            Id = 2,
            Color = "0000ff",
            Name = "Blue",
          },
        },
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("edited", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.NotNull(changeSummary, "should have generated change notification");
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        var labels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(2, labels.Count());
      };

      // Then remove the Red label.
      issue.Labels = issue.Labels.Where(x => !x.Name.Equals("Red"));
      changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("unlabeled", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      // Expect null if there are no changes to notify about.
      Assert.Null(changeSummary);

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        var labels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(1, labels.Count());
        Assert.AreEqual("Blue", labels[0].Name);
        Assert.AreEqual("0000ff", labels[0].Color);
      };

      // Then remove the last label.
      issue.Labels = new Label[] { };
      changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("unlabeled", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      // Expect null if there are no changes to notify about.
      Assert.Null(changeSummary);

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        var labels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(0, updatedIssue.Labels.Count());
      };
    }

    [Test]
    public async Task TestIssueHookCreatesMilestoneIfNeeded() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 1,
        Labels = new List<Label>(),
        User = new Account() {
          Id = user.Id,
          Login = user.Login,
          Type = GitHubAccountType.User,
        },
        Milestone = new Milestone() {
          Id = 5001,
          Number = 1234,
          State = "",
          Title = "some milestone",
          Description = "more info about some milestone",
          CreatedAt = DateTimeOffset.Parse("1/1/2016"),
          UpdatedAt = DateTimeOffset.Parse("1/2/2016"),
          DueOn = DateTimeOffset.Parse("2/1/2016"),
          ClosedAt = DateTimeOffset.Parse("3/1/2016"),
          Creator = new Account() {
            Id = user.Id,
            Login = user.Login,
            Type = GitHubAccountType.User,
          }
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("opened", issue, repo.Id), "repo", repo.Id, hook.Secret.ToString());

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var milestone = context.Milestones.First(x => x.Id == 5001);
        Assert.AreEqual("some milestone", milestone.Title);
        Assert.AreEqual("more info about some milestone", milestone.Description);
        Assert.AreEqual(1234, milestone.Number);
        Assert.AreEqual(DateTimeOffset.Parse("1/1/2016"), milestone.CreatedAt);
        Assert.AreEqual(DateTimeOffset.Parse("1/2/2016"), milestone.UpdatedAt);
        Assert.AreEqual(DateTimeOffset.Parse("2/1/2016"), milestone.DueOn);
        Assert.AreEqual(DateTimeOffset.Parse("3/1/2016"), milestone.ClosedAt);
      }
    }

    [Test]
    public async Task TestIssueHookCreatesAssigneesIfNeeded() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 1,
        Labels = new List<Label>(),
        User = new Account() {
          Id = user.Id,
          Login = user.Login,
          Type = GitHubAccountType.User,
        },
        Assignees = new Account[] {
          new Account() {
            Id = 11001,
            Login = "nobody1",
            Type = GitHubAccountType.User,
          },
          new Account() {
            Id = 11002,
            Login = "nobody2",
            Type = GitHubAccountType.User,
          },
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("opened", issue, repo.Id), "repo", repo.Id, hook.Secret.ToString());

      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var nobody1 = context.Accounts.Single(x => x.Id == 11001);
        var nobody2 = context.Accounts.Single(x => x.Id == 11002);

        Assert.AreEqual("nobody1", nobody1.Login);
        Assert.AreEqual("nobody2", nobody2.Login);
      }
    }

    [Test]
    public async Task TestIssueHookCreatesCreatorIfNeeded() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 1,
        Labels = new List<Label>(),
        User = new Account() {
          Id = 12001,
          Login = "nobody",
          Type = GitHubAccountType.User,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("opened", issue, repo.Id), "repo", repo.Id, hook.Secret.ToString());

      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var nobody1 = context.Accounts.Single(x => x.Id == 12001);
        Assert.AreEqual("nobody", nobody1.Login);
      }
    }

    [Test]
    public async Task TestIssueHookCreatesClosedByIfNeeded() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var issue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 1,
        Labels = new List<Label>(),
        User = new Account() {
          Id = user.Id,
          Login = user.Login,
          Type = GitHubAccountType.User,
        },
        ClosedBy = new Account() {
          Id = 13001,
          Login = "closedByNobody",
          Type = GitHubAccountType.User,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("opened", issue, repo.Id), "repo", repo.Id, hook.Secret.ToString());

      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var nobody1 = context.Accounts.Single(x => x.Id == 13001);
        Assert.AreEqual("closedByNobody", nobody1.Login);
      }
    }

    [Test]
    public async Task TestRepoCreatedTriggersSyncAccountRepositories() {
      Common.DataModel.User user1;
      Common.DataModel.User user2;
      Common.DataModel.Hook hook;
      Common.DataModel.Organization org;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user1 = TestUtil.MakeTestUser(context);
        user2 = (Common.DataModel.User)context.Accounts.Add(new Common.DataModel.User() {
          Id = 3002,
          Login = "alok",
          Date = DateTimeOffset.UtcNow,
          Token = Guid.NewGuid().ToString(),
        });
        org = TestUtil.MakeTestOrg(context);
        context.OrganizationAccounts.Add(new Common.DataModel.OrganizationAccount() {
          UserId = user1.Id,
          OrganizationId = org.Id,
        });
        context.OrganizationAccounts.Add(new Common.DataModel.OrganizationAccount() {
          UserId = user2.Id,
          OrganizationId = org.Id,
        });
        hook = MakeTestOrgHook(context, user1.Id, org.Id);
        await context.SaveChangesAsync();
      }

      var obj = JObject.FromObject(new {
        action = "created",
        repository = new Repository() {
          Id = 555,
          Owner = new Account() {
            Id = org.Id,
            Login = "loopt",
            Type = GitHubAccountType.Organization,
          },
          Name = "mix",
          FullName = "loopt/mix",
          Private = true,
          HasIssues = true,
          UpdatedAt = DateTimeOffset.Parse("1/1/2016"),
        },
      }, GitHubSerialization.JsonSerializer);

      var forceRepoSyncCalls = new List<long>();
      var mockGrainFactory = new Mock<IGrainFactory>();
      mockGrainFactory
        .Setup(x => x.GetGrain<IUserActor>(It.IsAny<long>(), It.IsAny<string>()))
        .Returns((long userId, string _) => {
          var userMock = new Mock<IUserActor>();
          userMock
            .Setup(x => x.ForceSyncRepositories())
            .Returns(Task.CompletedTask)
            .Callback(() => forceRepoSyncCalls.Add(userId));
          return userMock.Object;
        });

      var controller = new GitHubWebhookController(null, AutoMapper, mockGrainFactory.Object);
      ConfigureController(controller, "repository", obj, hook.Secret.ToString());
      var result = await controller.HandleHook("org", org.Id);
      Assert.IsInstanceOf(typeof(StatusCodeResult), result);
      Assert.AreEqual(HttpStatusCode.Accepted, ((StatusCodeResult)result).StatusCode);

      Assert.AreEqual(
        new List<long> {
          user1.Id,
          user2.Id,
        },
        forceRepoSyncCalls);
    }

    [Test]
    public async Task TestOrgRepoDeletionTriggersSyncAccountRepositories() {
      Common.DataModel.User user1;
      Common.DataModel.User user2;
      Common.DataModel.Hook orgHook;
      Common.DataModel.Hook repoHook;
      Common.DataModel.Repository repo;
      Common.DataModel.Organization org;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user1 = TestUtil.MakeTestUser(context);
        user2 = (Common.DataModel.User)context.Accounts.Add(new Common.DataModel.User() {
          Id = 3002,
          Login = "alok",
          Date = DateTimeOffset.UtcNow,
          Token = Guid.NewGuid().ToString(),
        });

        org = TestUtil.MakeTestOrg(context);
        context.OrganizationAccounts.Add(new Common.DataModel.OrganizationAccount() {
          UserId = user1.Id,
          OrganizationId = org.Id,
        });
        context.OrganizationAccounts.Add(new Common.DataModel.OrganizationAccount() {
          UserId = user2.Id,
          OrganizationId = org.Id,
        });
        repo = context.Repositories.Add(new Common.DataModel.Repository() {
          Id = 2001,
          Name = "mix",
          FullName = $"{org.Login}/mix",
          AccountId = org.Id,
          Private = true,
          Date = DateTimeOffset.UtcNow,
        });

        // In the case of repo deletions, we'll receive a webhook event
        // on both the org and repo hook.  So, let's have both in our test.
        orgHook = MakeTestOrgHook(context, user1.Id, org.Id);
        repoHook = MakeTestRepoHook(context, user1.Id, repo.Id);

        await context.SaveChangesAsync();
      }

      var obj = JObject.FromObject(new {
        action = "deleted",
        repository = new Repository() {
          Id = 555,
          Owner = new Account() {
            Id = org.Id,
            Login = org.Login,
            Type = GitHubAccountType.Organization,
          },
          Name = repo.Name,
          FullName = repo.FullName,
          Private = true,
          HasIssues = true,
          UpdatedAt = DateTimeOffset.Parse("1/1/2016"),
        },
      }, GitHubSerialization.JsonSerializer);

      var forceRepoSyncCalls = new List<long>();
      var mockGrainFactory = new Mock<IGrainFactory>();
      mockGrainFactory
        .Setup(x => x.GetGrain<IUserActor>(It.IsAny<long>(), It.IsAny<string>()))
        .Returns((long userId, string _) => {
          var userMock = new Mock<IUserActor>();
          userMock
            .Setup(x => x.ForceSyncRepositories())
            .Returns(Task.CompletedTask)
            .Callback(() => forceRepoSyncCalls.Add(userId));
          return userMock.Object;
        });

      // We register for the "repository" event on both the org and repo levels.
      // When a deletion happens, we'll get a webhook call for both the repo and
      // org, but we want to ignore the org one.
      var tests = new Tuple<string, long, Common.DataModel.Hook>[] {
        Tuple.Create("repo", repo.Id, repoHook),
        Tuple.Create("org", org.Id, orgHook),
      };

      foreach (var test in tests) {
        var controller = new GitHubWebhookController(null, AutoMapper, mockGrainFactory.Object);
        ConfigureController(controller, "repository", obj, test.Item3.Secret.ToString());
        var result = await controller.HandleHook(test.Item1, test.Item2);
        Assert.IsInstanceOf(typeof(StatusCodeResult), result);
        Assert.AreEqual(HttpStatusCode.Accepted, ((StatusCodeResult)result).StatusCode);
      }

      Assert.AreEqual(2, forceRepoSyncCalls.Count,
        "should have only 1 call for each user in the org");
      Assert.AreEqual(
        new List<long> {
          user1.Id,
          user2.Id,
        },
        forceRepoSyncCalls);
    }

    [Test]
    public async Task TestNonOrgRepoDeletionTriggersSyncAccountRepositories() {
      Common.DataModel.User user;
      Common.DataModel.Hook repoHook;
      Common.DataModel.Repository repo;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        repoHook = MakeTestRepoHook(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var obj = JObject.FromObject(new {
        action = "deleted",
        repository = new Repository() {
          Id = 555,
          Owner = new Account() {
            Id = user.Id,
            Login = user.Login,
            Type = GitHubAccountType.User,
          },
          Name = repo.Name,
          FullName = repo.FullName,
          Private = true,
          HasIssues = true,
          UpdatedAt = DateTimeOffset.Parse("1/1/2016"),
        },
      }, GitHubSerialization.JsonSerializer);

      var forceRepoSyncCalls = new List<long>();
      var mockGrainFactory = new Mock<IGrainFactory>();
      mockGrainFactory
        .Setup(x => x.GetGrain<IUserActor>(It.IsAny<long>(), It.IsAny<string>()))
        .Returns((long userId, string _) => {
          var userMock = new Mock<IUserActor>();
          userMock
            .Setup(x => x.ForceSyncRepositories())
            .Returns(Task.CompletedTask)
            .Callback(() => forceRepoSyncCalls.Add(userId));
          return userMock.Object;
        });

      var controller = new GitHubWebhookController(null, AutoMapper, mockGrainFactory.Object);
      ConfigureController(controller, "repository", obj, repoHook.Secret.ToString());
      var result = await controller.HandleHook("repo", repo.Id);
      Assert.IsInstanceOf(typeof(StatusCodeResult), result);
      Assert.AreEqual(HttpStatusCode.Accepted, ((StatusCodeResult)result).StatusCode);

      Assert.AreEqual(
        new List<long> {
          user.Id,
        },
        forceRepoSyncCalls,
        "should only trigger sync for repo owner");
    }

    private static JObject IssueCommentPayload(
      string action,
      Issue issue,
      Common.DataModel.Account user,
      Common.DataModel.Repository repo,
      Comment comment
      ) {
      return JObject.FromObject(new {
        action = action,
        issue = issue,
        comment = comment,
        repository = new Repository() {
          Id = repo.Id,
          Owner = new Account() {
            Id = user.Id,
            Login = user.Login,
            Type = GitHubAccountType.Organization,
          },
          Name = repo.Name,
          FullName = repo.FullName,
          Private = true,
          HasIssues = true,
          UpdatedAt = DateTimeOffset.Parse("1/1/2016"),
        },
      }, GitHubSerialization.JsonSerializer);
    }

    [Test]
    public async Task IssueCommentCreated() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        Common.DataModel.User user;
        Common.DataModel.Hook hook;
        Common.DataModel.Repository repo;
        Common.DataModel.Issue issue;

        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        issue = MakeTestIssue(context, user.Id, repo.Id);

        await context.SaveChangesAsync();

        var obj = IssueCommentPayload(
          "created",
          AutoMapper.Map<Issue>(issue),
          user,
          repo,
          new Comment() {
            Id = 9001,
            Body = "some comment body",
            CreatedAt = DateTimeOffset.Parse("1/1/2016"),
            UpdatedAt = DateTimeOffset.Parse("1/1/2016"),
            User = new Account() {
              Id = user.Id,
              Login = user.Login,
              Type = GitHubAccountType.User,
            },
            IssueUrl = $"https://api.github.com/repos/{repo.FullName}/issues/{issue.Number}",
          });
        IChangeSummary changeSummary = await ChangeSummaryFromHook("issue_comment", obj, "repo", repo.Id, hook.Secret.ToString());

        var comment = context.Comments.SingleOrDefault(x => x.IssueId == issue.Id);
        Assert.NotNull(comment, "should have created comment");

        Assert.AreEqual(new[] { repo.Id }, changeSummary.Repositories.ToArray());
      }
    }

    [Test]
    public async Task IssueCommentEdited() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        Common.DataModel.User user;
        Common.DataModel.Hook hook;
        Common.DataModel.Repository repo;
        Common.DataModel.Issue issue;

        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        issue = MakeTestIssue(context, user.Id, repo.Id);

        var comment = context.Comments.Add(new Common.DataModel.Comment() {
          Id = 9001,
          Body = "original body",
          CreatedAt = DateTimeOffset.Parse("1/1/2016"),
          UpdatedAt = DateTimeOffset.Parse("1/1/2016"),
          UserId = user.Id,
          IssueId = issue.Id,
          RepositoryId = repo.Id,
        });

        await context.SaveChangesAsync();

        var obj = IssueCommentPayload("created",
          AutoMapper.Map<Issue>(issue),
          user,
          repo,
          new Comment() {
            Id = 9001,
            Body = "edited body",
            CreatedAt = DateTimeOffset.Parse("1/1/2016"),
            UpdatedAt = DateTimeOffset.Parse("2/1/2016"),
            User = new Account() {
              Id = user.Id,
              Login = user.Login,
              Type = GitHubAccountType.User,
            },
            IssueUrl = $"https://api.github.com/repos/{repo.FullName}/issues/{issue.Number}",
          });
        IChangeSummary changeSummary = await ChangeSummaryFromHook("issue_comment", obj, "repo", repo.Id, hook.Secret.ToString());

        context.Entry(comment).Reload();

        Assert.AreEqual("edited body", comment.Body);
        Assert.AreEqual(DateTimeOffset.Parse("2/1/2016"), comment.UpdatedAt);

        Assert.AreEqual(new[] { repo.Id }, changeSummary.Repositories.ToArray());
      }
    }

    [Test]
    public async Task IssueCommentWillCreateCommentAuthorIfNeeded() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        Common.DataModel.User user;
        Common.DataModel.Hook hook;
        Common.DataModel.Repository repo;
        Common.DataModel.Issue issue;

        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        issue = MakeTestIssue(context, user.Id, repo.Id);

        await context.SaveChangesAsync();

        var obj = IssueCommentPayload(
          "created",
          AutoMapper.Map<Issue>(issue),
          user,
          repo,
          new Comment() {
            Id = 9001,
            Body = "comment body",
            CreatedAt = DateTimeOffset.Parse("1/1/2016"),
            UpdatedAt = DateTimeOffset.Parse("2/1/2016"),
            User = new Account() {
              Id = 555,
              Login = "alok",
              Type = GitHubAccountType.User,
            },
            IssueUrl = $"https://api.github.com/repos/{repo.FullName}/issues/{issue.Number}",
          });
        IChangeSummary changeSummary = await ChangeSummaryFromHook("issue_comment", obj, "repo", repo.Id, hook.Secret.ToString());

        var comment = context.Comments.SingleOrDefault(x => x.Id == 9001);
        Assert.NotNull(comment, "should have created comment");
        Assert.AreEqual("comment body", comment.Body);

        var commentAuthor = context.Accounts.SingleOrDefault(x => x.Id == 555);
        Assert.NotNull(comment, "should have created comment");
        Assert.AreEqual("alok", commentAuthor.Login);

        Assert.AreEqual(new[] { repo.Id }, changeSummary.Repositories.ToArray());
      }
    }

    [Test]
    public async Task IssueCommentDeleted() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        Common.DataModel.User user;
        Common.DataModel.Hook hook;
        Common.DataModel.Repository repo;
        Common.DataModel.Issue issue;

        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        issue = MakeTestIssue(context, user.Id, repo.Id);

        await context.SaveChangesAsync();

        // Have to use official channels if you actually want correct change notifications.
        await context.BulkUpdateComments(repo.Id, new[] {
          new CommentTableType() {
            Id = 9001,
            Body = "comment body #1",
            CreatedAt = DateTimeOffset.Parse("1/1/2016"),
            UpdatedAt = DateTimeOffset.Parse("1/1/2016"),
            UserId = user.Id,
            IssueId = issue.Id,
          },
          new CommentTableType() {
            Id = 9002,
            Body = "comment body #2",
            CreatedAt = DateTimeOffset.Parse("1/1/2016"),
            UpdatedAt = DateTimeOffset.Parse("1/1/2016"),
            UserId = user.Id,
            IssueId = issue.Id,
          },
        });

        var obj = IssueCommentPayload(
          "deleted",
          AutoMapper.Map<Issue>(issue),
          user,
          repo,
          new Comment() {
            Id = 9001,
            Body = "comment body",
            CreatedAt = DateTimeOffset.Parse("1/1/2016"),
            UpdatedAt = DateTimeOffset.Parse("1/1/2016"),
            User = new Account() {
              Id = user.Id,
              Login = user.Login,
              Type = GitHubAccountType.User,
            },
            IssueUrl = $"https://api.github.com/repos/{repo.FullName}/issues/{issue.Number}",
          });
        IChangeSummary changeSummary = await ChangeSummaryFromHook("issue_comment", obj, "repo", repo.Id, hook.Secret.ToString());

        Assert.AreEqual(
          new long[] { 9002 },
          context.Comments
            .Where(x => x.IssueId == issue.Id)
            .Select(x => x.Id).ToArray(),
          "only one comment should have been deleted");

        Assert.AreEqual(new[] { repo.Id }, changeSummary.Repositories.ToArray());
      }
    }

    [Test]
    public async Task IssueCommentWillCreateIssueIfNeeded() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        Common.DataModel.User user;
        Common.DataModel.Hook hook;
        Common.DataModel.Repository repo;

        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);

        await context.SaveChangesAsync();

        var obj = IssueCommentPayload(
          "created",
          new Issue() {
            Id = 1001,
            Title = "Some Title",
            Body = "Some Body",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            State = "open",
            Number = 1234,
            Labels = new List<Label>(),
            User = new Account() {
              Id = user.Id,
              Login = user.Login,
              Type = GitHubAccountType.User,
            },
          },
          user,
          repo,
          new Comment() {
            Id = 9001,
            Body = "comment body",
            CreatedAt = DateTimeOffset.Parse("1/1/2016"),
            UpdatedAt = DateTimeOffset.Parse("2/1/2016"),
            User = new Account() {
              Id = user.Id,
              Login = user.Login,
              Type = GitHubAccountType.User,
            },
            IssueUrl = $"https://api.github.com/repos/{repo.FullName}/issues/1234",
          });
        IChangeSummary changeSummary = await ChangeSummaryFromHook("issue_comment", obj, "repo", repo.Id, hook.Secret.ToString());

        var issue = context.Issues.SingleOrDefault(x => x.Number == 1234);
        Assert.NotNull(issue, "should have created issue referenced by comment.");

        Assert.AreEqual(new long[] { repo.Id }, changeSummary.Repositories.ToArray());
      }
    }

    [Test]
    public async Task MilestoneCreated() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var payload = new {
        action = "created",
        milestone = new Milestone() {
          Id = 5001,
          Number = 1234,
          State = "open",
          Title = "new milestone",
          Description = "some description",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 2, 0, 0, 0, TimeSpan.Zero),
          ClosedAt = new DateTimeOffset(2017, 1, 3, 0, 0, 0, TimeSpan.Zero),
          DueOn = new DateTimeOffset(2017, 1, 4, 0, 0, 0, TimeSpan.Zero),
          Creator = new Account() {
            Id = user.Id,
            Login = user.Login,
            Type = GitHubAccountType.User,
          },
        },
        repository = new {
          id = repo.Id,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromHook(
        "milestone",
        JObject.FromObject(payload, GitHubSerialization.JsonSerializer),
        "repo",
        repo.Id,
        hook.Secret.ToString());

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var newMilestone = context.Milestones.First();
        Assert.AreEqual(5001, newMilestone.Id);
        Assert.AreEqual(1234, newMilestone.Number);
        Assert.AreEqual("new milestone", newMilestone.Title);
        Assert.AreEqual("some description", newMilestone.Description);
        Assert.AreEqual("open", newMilestone.State);
        Assert.AreEqual(repo.Id, newMilestone.RepositoryId);
        Assert.AreEqual(new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero), newMilestone.CreatedAt);
        Assert.AreEqual(new DateTimeOffset(2017, 1, 2, 0, 0, 0, TimeSpan.Zero), newMilestone.UpdatedAt);
        Assert.AreEqual(new DateTimeOffset(2017, 1, 3, 0, 0, 0, TimeSpan.Zero), newMilestone.ClosedAt);
        Assert.AreEqual(new DateTimeOffset(2017, 1, 4, 0, 0, 0, TimeSpan.Zero), newMilestone.DueOn);
      }
    }

    [Test]
    public async Task MilestoneDeleted() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;
      Common.DataModel.Milestone milestone;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        milestone = context.Milestones.Add(new Common.DataModel.Milestone() {
          Id = 5001,
          RepositoryId = repo.Id,
          Number = 1234,
          State = "open",
          Title = "some milestone",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await context.SaveChangesAsync();
      }

      var payload = new {
        action = "deleted",
        milestone = new Milestone() {
          Id = 5001,
          Number = 1234,
          State = "open",
          Title = "some milestone",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 2, 0, 0, 0, TimeSpan.Zero),
          Creator = new Account() {
            Id = user.Id,
            Login = user.Login,
            Type = GitHubAccountType.User,
          },
        },
        repository = new {
          id = repo.Id,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromHook(
        "milestone",
        JObject.FromObject(payload, GitHubSerialization.JsonSerializer),
        "repo",
        repo.Id,
        hook.Secret.ToString());

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var deletedMilestone = context.Milestones.SingleOrDefault(x => x.Id == milestone.Id);
        Assert.Null(deletedMilestone);
      }

      changeSummary = await ChangeSummaryFromHook(
        "milestone",
        JObject.FromObject(payload, GitHubSerialization.JsonSerializer),
        "repo",
        repo.Id,
        hook.Secret.ToString());
      Assert.Null(changeSummary,
        "if we try to delete an already deleted milestone, should see no change");
    }

    [Test]
    public async Task MilestoneEdited() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;
      Common.DataModel.Milestone milestone;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        milestone = context.Milestones.Add(new Common.DataModel.Milestone() {
          Id = 5001,
          RepositoryId = repo.Id,
          Number = 1234,
          State = "open",
          Title = "some milestone",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await context.SaveChangesAsync();
      }

      var payload = new {
        action = "edited",
        milestone = new Milestone() {
          Id = 5001,
          Number = 1234,
          State = "open",
          Title = "some milestone edited",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 2, 0, 0, 0, TimeSpan.Zero),
          Creator = new Account() {
            Id = user.Id,
            Login = user.Login,
            Type = GitHubAccountType.User,
          },
        },
        repository = new {
          id = repo.Id,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromHook(
        "milestone",
        JObject.FromObject(payload, GitHubSerialization.JsonSerializer),
        "repo",
        repo.Id,
        hook.Secret.ToString());

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var editedMilestone = context.Milestones.First();
        Assert.AreEqual("some milestone edited", editedMilestone.Title);
      }
    }

    [Test]
    public async Task MilestoneClosed() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;
      Common.DataModel.Milestone milestone;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        milestone = context.Milestones.Add(new Common.DataModel.Milestone() {
          Id = 5001,
          RepositoryId = repo.Id,
          Number = 1234,
          State = "open",
          Title = "some milestone",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await context.SaveChangesAsync();
      }

      var payload = new {
        action = "edited",
        milestone = new Milestone() {
          Id = 5001,
          Number = 1234,
          State = "closed",
          Title = "some milestone",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 2, 0, 0, 0, TimeSpan.Zero),
          Creator = new Account() {
            Id = user.Id,
            Login = user.Login,
            Type = GitHubAccountType.User,
          },
        },
        repository = new {
          id = repo.Id,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromHook(
        "milestone",
        JObject.FromObject(payload, GitHubSerialization.JsonSerializer),
        "repo",
        repo.Id,
        hook.Secret.ToString());

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var editedMilestone = context.Milestones.First();
        Assert.AreEqual("closed", editedMilestone.State);
      }
    }

    [Test]
    public async Task MilestoneOpened() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;
      Common.DataModel.Milestone milestone;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        milestone = context.Milestones.Add(new Common.DataModel.Milestone() {
          Id = 5001,
          RepositoryId = repo.Id,
          Number = 1234,
          State = "closed",
          Title = "some milestone",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await context.SaveChangesAsync();
      }

      var payload = new {
        action = "opened",
        milestone = new Milestone() {
          Id = 5001,
          Number = 1234,
          State = "open",
          Title = "some milestone",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 2, 0, 0, 0, TimeSpan.Zero),
          Creator = new Account() {
            Id = user.Id,
            Login = user.Login,
            Type = GitHubAccountType.User,
          },
        },
        repository = new {
          id = repo.Id,
        },
      };

      IChangeSummary changeSummary = await ChangeSummaryFromHook(
        "milestone",
        JObject.FromObject(payload, GitHubSerialization.JsonSerializer),
        "repo",
        repo.Id,
        hook.Secret.ToString());

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var editedMilestone = context.Milestones.First();
        Assert.AreEqual("open", editedMilestone.State);
      }
    }
  }
}
