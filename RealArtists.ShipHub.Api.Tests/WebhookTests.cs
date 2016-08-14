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
  using AutoMapper;
  using Moq;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
  using RealArtists.ShipHub.Api.Controllers;
  using RealArtists.ShipHub.Common.DataModel.Types;
  using RealArtists.ShipHub.Common.GitHub;
  using RealArtists.ShipHub.Common.GitHub.Models;
  using RealArtists.ShipHub.QueueClient;
  using Xunit;

  public class WebhookTests {

    private static string SignatureForPayload(string key, string payload) {
      var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
      byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
      return "sha1=" + new SoapHexBinary(hash).ToString();
    }

    private static IMapper AutoMapper() {
      var config = new MapperConfiguration(cfg => {
        cfg.AddProfile<Common.DataModel.GitHubToDataModelProfile>();
      });
      var mapper = config.CreateMapper();
      return mapper;
    }

    private static void ConfigureController(ApiController controller, string eventName, JObject body, string secretKey) {
      var json = JsonConvert.SerializeObject(body, GitHubClient.JsonSettings);
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

    private static async Task<IChangeSummary> ChangeSummaryFromIssuesHook(JObject obj, string repoOrOrg, long repoOrOrgId, string secret) {
      IChangeSummary changeSummary = null;

      var mockBusClient = new Mock<IShipHubBusClient>();
      mockBusClient.Setup(x => x.NotifyChanges(It.IsAny<IChangeSummary>()))
        .Returns(Task.CompletedTask)
        .Callback((IChangeSummary arg) => { changeSummary = arg; });

      var controller = new GitHubWebhookController(mockBusClient.Object);
      ConfigureController(controller, "issues", obj, secret);

      IHttpActionResult result = await controller.HandleHook(repoOrOrg, repoOrOrgId);
      Assert.IsType<StatusCodeResult>(result);
      Assert.Equal(HttpStatusCode.Accepted, (result as StatusCodeResult).StatusCode);

      return changeSummary;
    }

    private static JObject IssueChange(string action, Issue issue, long repositoryId) {
      var obj = new {
        action = "opened",
        issue = issue,
        repository = new {
          id = repositoryId,
        },
      };
      return JObject.FromObject(obj, JsonSerializer.CreateDefault(GitHubClient.JsonSettings));
    }

    private static Common.DataModel.Organization MakeTestOrg(Common.DataModel.ShipHubContext context) {
      return (Common.DataModel.Organization)context.Accounts.Add(new Common.DataModel.Organization() {
        Id = 6001,
        Login = "myorg",
        Date = DateTimeOffset.Now,
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
      };
      context.Issues.Add(issue);
      return issue;
    }

    private Common.DataModel.Hook MakeTestRepoHook(Common.DataModel.ShipHubContext context, long creatorId, long repoId) {
      return context.Hooks.Add(new Common.DataModel.Hook() {
        Secret = Guid.NewGuid(),
        Active = true,
        Events = "event1,event2",
        RepositoryId = repoId,
      });
    }

    private Common.DataModel.Hook MakeTestOrgHook(Common.DataModel.ShipHubContext context, long creatorId, long orgId) {
      return context.Hooks.Add(new Common.DataModel.Hook() {
        Secret = Guid.NewGuid(),
        Active = true,
        Events = "event1,event2",
        OrganizationId = orgId,
      });
    }

    [Fact]
    [AutoRollback]
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
        }, JsonSerializer.CreateDefault(GitHubClient.JsonSettings));

        var controller = new GitHubWebhookController();
        ConfigureController(controller, "ping", obj, hook.Secret.ToString());
        var result = await controller.HandleHook("repo", repo.Id);
        Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(HttpStatusCode.Accepted, ((StatusCodeResult)result).StatusCode);
      }
    }

    [Fact]
    [AutoRollback]
    public async Task TestPingSucceedsIfSignatureMatchesOrgHook() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var org = MakeTestOrg(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = MakeTestOrgHook(context, user.Id, org.Id);
        org.Members.Add(user);
        await context.SaveChangesAsync();

        var obj = JObject.FromObject(new {
          hook_id = 1234,
          repository = new {
            id = repo.Id,
          },
          organization = new {
            id = org.Id,
          },
        }, JsonSerializer.CreateDefault(GitHubClient.JsonSettings));

        var controller = new GitHubWebhookController();
        ConfigureController(controller, "ping", obj, hook.Secret.ToString());
        var result = await controller.HandleHook("org", org.Id);
        Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(HttpStatusCode.Accepted, ((StatusCodeResult)result).StatusCode);
      }
    }

    [Fact]
    [AutoRollback]
    public async Task TestPingFailsWithInvalidSignature() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);

        var hook = context.Hooks.Add(new Common.DataModel.Hook() {
          Secret = Guid.NewGuid(),
          Active = true,
          Events = "some events",
          RepositoryId = repo.Id,
        });

        await context.SaveChangesAsync();

        var obj = JObject.FromObject(new {
          hook_id = 1234,
          repository = new {
            id = repo.Id,
          },
        }, JsonSerializer.CreateDefault(GitHubClient.JsonSettings));

        var controller = new GitHubWebhookController();
        ConfigureController(controller, "ping", obj, "someIncorrectSignature");
        var result = await controller.HandleHook("repo", repo.Id);
        Assert.IsType<BadRequestErrorMessageResult>(result);
        Assert.Equal("Invalid signature.", ((BadRequestErrorMessageResult)result).Message);
      }
    }

    [Fact]
    [AutoRollback]
    public async Task TestWebhookCallUpdatesLastSeen() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = MakeTestRepoHook(context, user.Id, repo.Id);

        await context.SaveChangesAsync();

        Assert.Null(hook.LastSeen);

        var obj = new JObject(
        new JProperty("zen", "It's not fully shipped until it's fast."),
        new JProperty("hook_id", 1234),
        new JProperty("hook", null),
        new JProperty("sender", null),
        new JProperty("repository", new JObject(
          new JProperty("id", repo.Id)
          )));

        var controller = new GitHubWebhookController();
        ConfigureController(controller, "ping", obj, hook.Secret.ToString());
        var result = await controller.HandleHook("repo", repo.Id);
        Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(HttpStatusCode.Accepted, ((StatusCodeResult)result).StatusCode);

        context.Entry(hook).Reload();
        Assert.NotNull(hook.LastSeen);
      }
    }

    [Fact]
    [AutoRollback]
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
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

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var newIssue = context.Issues.First();
        Assert.Equal(1001, newIssue.Id);
        Assert.Equal(1, newIssue.Number);
        Assert.Equal("Some Title", newIssue.Title);
        Assert.Equal("Some Body", newIssue.Body);
        Assert.Equal("open", newIssue.State);
        Assert.Equal(2001, newIssue.RepositoryId);
        Assert.Equal(3001, newIssue.UserId);
      }
    }

    [Fact]
    [AutoRollback]
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
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

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.Equal("closed", updatedIssue.State);
      };
    }

    [Fact]
    [AutoRollback]
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
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

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.Equal("open", updatedIssue.State);
      }
    }

    [Fact]
    [AutoRollback]
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
        State = "open",
        Number = 5,
        Labels = new List<Label> {
          new Label() {
            Color = "ff0000",
            Name = "Red",
          },
          new Label() {
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

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.Equal("A New Title", updatedIssue.Title);
        Assert.Equal("A New Body", updatedIssue.Body);
      };
    }

    [Fact]
    [AutoRollback]
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
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

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.Equal(testUser.Id, updatedIssue.Assignees.First().Id);
      }
    }

    [Fact]
    [AutoRollback]
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
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

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        Assert.Empty(updatedIssue.Assignees);
      }
    }

    [Fact]
    [AutoRollback]
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
        State = "open",
        Number = 5,
        Labels = new List<Label> {
          new Label() {
            Color = "ff0000",
            Name = "Red",
          },
          new Label() {
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

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        var labels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.Equal(2, labels.Count());
        Assert.Equal("Blue", labels[0].Name);
        Assert.Equal("0000ff", labels[0].Color);
        Assert.Equal("Red", labels[1].Name);
        Assert.Equal("ff0000", labels[1].Color);
      };
    }

    [Fact]
    [AutoRollback]
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
        State = "open",
        Number = 5,
        Labels = new List<Label> {
          new Label() {
            Color = "ff0000",
            Name = "Red",
          },
          new Label() {
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

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        var labels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.Equal(2, labels.Count());
      };

      // Then remove the Red label.
      issue.Labels = issue.Labels.Where(x => !x.Name.Equals("Red"));
      changeSummary = await ChangeSummaryFromIssuesHook(IssueChange("unlabeled", issue, testRepo.Id), "repo", testRepo.Id, testHook.Secret.ToString());

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(0, changeSummary.Repositories.Count());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        var labels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.Equal(1, labels.Count());
        Assert.Equal("Blue", labels[0].Name);
        Assert.Equal("0000ff", labels[0].Color);
      };
    }

    [Fact]
    [AutoRollback]
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
        CreatedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
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

      Assert.Equal(0, changeSummary.Organizations.Count());
      Assert.Equal(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var milestone = context.Milestones.First(x => x.Id == 5001);
        Assert.Equal("some milestone", milestone.Title);
        Assert.Equal("more info about some milestone", milestone.Description);
        Assert.Equal(1234, milestone.Number);
        Assert.Equal(DateTimeOffset.Parse("1/1/2016"), milestone.CreatedAt);
        Assert.Equal(DateTimeOffset.Parse("1/2/2016"), milestone.UpdatedAt);
        Assert.Equal(DateTimeOffset.Parse("2/1/2016"), milestone.DueOn);
        Assert.Equal(DateTimeOffset.Parse("3/1/2016"), milestone.ClosedAt);
      }
    }

  }
}
