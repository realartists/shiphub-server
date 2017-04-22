namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Security.Cryptography;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces;
  using AutoMapper;
  using Common;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Controllers;
  using Microsoft.Azure.WebJobs;
  using Moq;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;
  using NUnit.Framework;
  using Orleans;
  using QueueClient;
  using QueueClient.Messages;
  using QueueProcessor.Jobs;
  using QueueProcessor.Tracing;

  [TestFixture]
  [AutoRollback]
  public class GitHubWebhookQueueHandlerTests {
    private static IMapper AutoMapper { get; } = CreateMapper();

    private static IMapper CreateMapper() {
      var config = new MapperConfiguration(cfg => {
        cfg.AddProfile<Common.DataModel.GitHubToDataModelProfile>();
        cfg.AddProfile<Sync.Messages.DataModelToApiModelProfile>();

        cfg.CreateMap<Common.DataModel.Milestone, Milestone>(MemberList.Destination);
        cfg.CreateMap<Common.DataModel.Issue, Issue>(MemberList.Destination)
          .ForMember(dest => dest.Reactions, o => o.ResolveUsing(src => src.Reactions?.DeserializeObject<ReactionSummary>()))
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

    private GitHubWebhookEventMessage CreateMessage(Common.DataModel.Hook hook, string eventName, JObject payload) {
      return CreateMessage(
        (hook.RepositoryId != null) ? "repo" : "org",
        (hook.RepositoryId != null) ? hook.RepositoryId.Value : hook.OrganizationId.Value,
        eventName,
        hook.Secret.ToString(),
        payload);
    }

    private GitHubWebhookEventMessage CreateMessage(string entityType, long entityId, string eventName, string secret, JObject payload) {
      var json = JsonConvert.SerializeObject(
        payload,
        GitHubSerialization.JsonSerializerSettings);

      var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
      var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));

      return new GitHubWebhookEventMessage() {
        EntityType = entityType,
        EntityId = entityId,
        EventName = eventName,
        DeliveryId = Guid.NewGuid(),
        Payload = json,
        Signature = signature,
      };
    }

    private async Task<ChangeMessage> ProcessEvent(GitHubWebhookEventMessage message, IGrainFactory grainFactory = null) {
      var changeMessages = new List<ChangeMessage>();
      var collectorMock = new Mock<IAsyncCollector<ChangeMessage>>();
      collectorMock.Setup(x => x.AddAsync(It.IsAny<ChangeMessage>(), It.IsAny<CancellationToken>()))
        .Returns((ChangeMessage msg, CancellationToken token) => {
          changeMessages.Add(msg);
          return Task.CompletedTask;
        });

      var executionContext = new Microsoft.Azure.WebJobs.ExecutionContext() { InvocationId = Guid.NewGuid() };
      var handler = new GitHubWebhookQueueHandler(grainFactory, AutoMapper, new DetailedExceptionLogger());
      await handler.ProcessEvent(message, collectorMock.Object, Console.Out, executionContext);

      if (changeMessages.Count == 0) {
        return null;
      } else if (changeMessages.Count == 1) {
        return changeMessages.Single();
      } else {
        throw new Exception("Should only send a single change message");
      }
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

        await ProcessEvent(CreateMessage(hook, "ping", obj));
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

        await ProcessEvent(CreateMessage(hook, "ping", obj));
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

        try {
          await ProcessEvent(CreateMessage("repo", repo.Id, "ping", "someIncorrectSignature", obj));
          Assert.Fail("should have thrown exception due to invalid signature");
        } catch (Exception) {
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

        await ProcessEvent(CreateMessage(hook, "ping", obj));

        context.Entry(hook).Reload();
        Assert.Greater(hook.LastSeen, DateTimeOffset.Parse("1/1/2000"));
        Assert.IsNull(hook.PingCount);
        Assert.IsNull(hook.LastPing);
      }
    }

    [Test]
    public async Task WillSilentlyFailIfEventDoesNotMatchKnownRepoOrOrg() {
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

        await ProcessEvent(CreateMessage("repo", repo.Id, "ping", "someSignature", obj));
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
        CreatedAt = testIssue.CreatedAt,
        UpdatedAt = testIssue.UpdatedAt.AddMinutes(1),
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

      var changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("milestoned", issue, testRepo.Id)));

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
        CreatedAt = testIssue.CreatedAt,
        UpdatedAt = testIssue.UpdatedAt.AddMinutes(1),
        State = "open",
        Number = 5,
        Labels = new List<Label>(),
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
      };

      var changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("demilestoned", issue, testRepo.Id)));

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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", issue, repo.Id)));

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
        CreatedAt = testIssue.CreatedAt,
        UpdatedAt = testIssue.UpdatedAt.AddMinutes(1),
        State = "closed",
        Number = 5,
        Labels = new List<Label>(),
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
      };

      var changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("closed", issue, testRepo.Id)));

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
        CreatedAt = testIssue.CreatedAt,
        UpdatedAt = testIssue.UpdatedAt.AddMinutes(1),
        State = "open",
        Number = 5,
        Labels = new List<Label>(),
        User = new Account() {
          Id = testUser.Id,
          Login = testUser.Login,
          Type = GitHubAccountType.User,
        },
      };

      var changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("reopened", issue, testRepo.Id)));

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
        CreatedAt = testIssue.CreatedAt,
        UpdatedAt = testIssue.UpdatedAt.AddMinutes(1),
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

      var changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("edited", issue, testRepo.Id)));

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
        CreatedAt = testIssue.CreatedAt,
        UpdatedAt = testIssue.UpdatedAt.AddMinutes(1),
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

      var changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("assigned", issue, testRepo.Id)));

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
        CreatedAt = testIssue.CreatedAt,
        UpdatedAt = testIssue.UpdatedAt.AddMinutes(1),
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

      var changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("unassigned", issue, testRepo.Id)));

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
        CreatedAt = testIssue.CreatedAt,
        UpdatedAt = testIssue.UpdatedAt.AddMinutes(1),
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

      var changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("labeled", issue, testRepo.Id)));

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
        CreatedAt = testIssue.CreatedAt,
        UpdatedAt = testIssue.UpdatedAt.AddMinutes(1),
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

      var changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("edited", issue, testRepo.Id)));

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
      changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("unlabeled", issue, testRepo.Id)));

      // Adding or removing a label changes the issue
      Assert.NotNull(changeSummary, "should have generated change notification");
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedIssue = context.Issues.Where(x => x.Id == testIssue.Id).First();
        var labels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(1, labels.Count());
        Assert.AreEqual("Blue", labels[0].Name);
        Assert.AreEqual("0000ff", labels[0].Color);
      };

      // Then remove the last label.
      issue.Labels = new Label[] { };
      changeSummary = await ProcessEvent(CreateMessage(testHook, "issues", IssueChange("unlabeled", issue, testRepo.Id)));

      // Adding or removing a label changes the issue
      Assert.NotNull(changeSummary, "should have generated change notification");
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { testRepo.Id }, changeSummary.Repositories.ToArray());

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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", issue, repo.Id)));

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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", issue, repo.Id)));

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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", issue, repo.Id)));

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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", issue, repo.Id)));

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
        user2 = TestUtil.MakeTestUser(context, 3002, "alok");
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

      await ProcessEvent(CreateMessage(hook, "repository", obj), mockGrainFactory.Object);

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
        user2 = TestUtil.MakeTestUser(context, 3002, "alok");
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
      var tests = new[] {
        repoHook,
        orgHook,
      };

      foreach (var hook in tests) {
        await ProcessEvent(CreateMessage(hook, "repository", obj), mockGrainFactory.Object);
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

      await ProcessEvent(CreateMessage(repoHook, "repository", obj), mockGrainFactory.Object);

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
      IssueComment comment
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
          new IssueComment() {
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
        var changeSummary = await ProcessEvent(CreateMessage(hook, "issue_comment", obj));

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
          new IssueComment() {
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
        var changeSummary = await ProcessEvent(CreateMessage(hook, "issue_comment", obj));

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
          new IssueComment() {
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
        var changeSummary = await ProcessEvent(CreateMessage(hook, "issue_comment", obj));

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
          new IssueComment() {
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
        var changeSummary = await ProcessEvent(CreateMessage(hook, "issue_comment", obj));

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
          new IssueComment() {
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
        var changeSummary = await ProcessEvent(CreateMessage(hook, "issue_comment", obj));

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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "milestone", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));
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
      Common.DataModel.Issue issue;
      MilestoneTableType milestone;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        issue = MakeTestIssue(context, user.Id, repo.Id);
        await context.SaveChangesAsync();

        // Have to use official methods to make the repo log entries.
        milestone = new MilestoneTableType() {
          Id = 5001,
          Number = 1234,
          State = "open",
          Title = "some milestone",
          Description = "whatever",
          CreatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
          UpdatedAt = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        await context.BulkUpdateMilestones(repo.Id, new[] { milestone });

        // Ensure foreign keys are properly handled.
        issue.MilestoneId = milestone.Id;
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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "milestone", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var deletedMilestone = context.Milestones.SingleOrDefault(x => x.Id == milestone.Id);
        Assert.Null(deletedMilestone);
      }

      changeSummary = await ProcessEvent(CreateMessage(hook, "milestone", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));
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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "milestone", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));

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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "milestone", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));

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

      var changeSummary = await ProcessEvent(CreateMessage(hook, "milestone", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { 2001 }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var editedMilestone = context.Milestones.First();
        Assert.AreEqual("open", editedMilestone.State);
      }
    }

    [Test]
    public async Task LabelCreated() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = MakeTestRepoHook(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
        await context.BulkUpdateLabels(repo.Id, new[] {
          new LabelTableType() {
            Id = 1001,
            Name = "blue",
            Color = "0000ff",
          },
        });

        var payload = new {
          action = "created",
          label = new Label() {
            Color = "ff0000",
            Name = "red",
          },
          repository = new {
            id = repo.Id,
          },
        };

        var changeSummary = await ProcessEvent(CreateMessage(hook, "label", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));

        Assert.AreEqual(0, changeSummary.Organizations.Count());
        Assert.AreEqual(new long[] { repo.Id }, changeSummary.Repositories.ToArray());

        context.Entry(repo).Reload();
        var labels = repo.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(2, labels.Count());
        Assert.AreEqual("blue", labels[0].Name);
        Assert.AreEqual("0000ff", labels[0].Color);
        Assert.AreEqual("red", labels[1].Name);
        Assert.AreEqual("ff0000", labels[1].Color);
      }
    }

    [Test]
    public async Task LabelCreatedButAlreadyExists() {
      using (var context = new Common.DataModel.ShipHubContext()) {
        var user = TestUtil.MakeTestUser(context);
        var repo = TestUtil.MakeTestRepo(context, user.Id);
        var hook = MakeTestRepoHook(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
        await context.BulkUpdateLabels(repo.Id, new[] {
          new LabelTableType() {
            Id = 1001,
            Name = "blue",
            Color = "0000ff",
          },
        });

        var payload = new {
          action = "created",
          label = new Label() {
            Id = 1001,
            Color = "0000ff",
            Name = "blue",
          },
          repository = new {
            id = repo.Id,
          },
        };

        var changeSummary = await ProcessEvent(CreateMessage(hook, "label", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));

        Assert.Null(changeSummary);

        context.Entry(repo).Reload();
        var labels = repo.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(1, labels.Count());
        Assert.AreEqual(1001, labels[0].Id);
        Assert.AreEqual("blue", labels[0].Name);
        Assert.AreEqual("0000ff", labels[0].Color);
      }
    }

    [Test]
    public async Task LabelDeleted() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;
      Common.DataModel.Issue issue;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        issue = MakeTestIssue(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var purpleLabel = new Label() {
        Id = 10021,
        Color = "ff00ff",
        Name = "purple",
      };

      var issueToNotDisturb = new Issue() {
        Id = 1002,
        Title = "Other Issue",
        Body = "",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 2,
        Labels = new[] {
          purpleLabel,
        },
        User = new Account() {
          Id = user.Id,
          Login = user.Login,
          Type = GitHubAccountType.User,
        },
      };

      // Make an issue with label purple.  We're not going to send
      // any changes related to this issue - we just want to make sure
      // it doesn't get disturbed by changes to other issues.
      await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", issueToNotDisturb, repo.Id)));

      var blueLabel = new Label() {
        Id = 2002,
        Color = "0000ff",
        Name = "blue",
      };
      var redLabel = new Label() {
        Id = 2001,
        Color = "ff0000",
        Name = "red",
      };

      // Pretend an issue was just opened.  Later, we'll pretend one of
      // its labels was deleted.
      var githubIssue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 5,
        Labels = new[] { redLabel, blueLabel },
        User = new Account() {
          Id = user.Id,
          Login = user.Login,
          Type = GitHubAccountType.User,
        },
      };

      await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", githubIssue, repo.Id)));

      var payload = new {
        action = "deleted",
        label = blueLabel,
        repository = new {
          id = repo.Id,
        },
      };
      var changeSummary = await ProcessEvent(CreateMessage(hook, "label", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));

      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { repo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedRepo = context.Repositories.Single(x => x.Id == repo.Id);
        var labels = updatedRepo.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(2, labels.Count());
        // Purple tag should remain since it was attached to another issue.
        Assert.AreEqual(purpleLabel.Id, labels[0].Id);
        Assert.AreEqual(purpleLabel.Name, labels[0].Name);
        Assert.AreEqual(purpleLabel.Color, labels[0].Color);
        Assert.AreEqual(redLabel.Id, labels[1].Id);
        Assert.AreEqual(redLabel.Name, labels[1].Name);
        Assert.AreEqual(redLabel.Color, labels[1].Color);

        var updatedIssue = context.Issues.Single(x => x.Id == issue.Id);
        var issueLabels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(1, issueLabels.Count());
        Assert.AreEqual(redLabel.Id, issueLabels[0].Id);
        Assert.AreEqual(redLabel.Name, issueLabels[0].Name);
        Assert.AreEqual(redLabel.Color, issueLabels[0].Color);
      }
    }

    [Test]
    public async Task LabelDeletedButAlreadyDeleted() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;
      Common.DataModel.Issue issue;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        issue = MakeTestIssue(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var payload = new {
        action = "deleted",
        label = new Label() {
          Id = 1001,
          Color = "0000ff",
          Name = "blue",
        },
        repository = new {
          id = repo.Id,
        },
      };

      var changeSummary = await ProcessEvent(CreateMessage(hook, "label", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));
      Assert.Null(changeSummary);

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedRepo = context.Repositories.Single(x => x.Id == repo.Id);
        Assert.AreEqual(0, updatedRepo.Labels.Count);
      }
    }

    [Test]
    public async Task LabelEdited() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;
      Common.DataModel.Issue issue;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        issue = MakeTestIssue(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var purpleLabel = new Label() {
        Id = 5001,
        Color = "ff00ff",
        Name = "purple",
      };

      var issueNotToDisturb = new Issue() {
        Id = 1002,
        Title = "Other Issue",
        Body = "",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 2,
        Labels = new[] { purpleLabel },
        User = new Account() {
          Id = user.Id,
          Login = user.Login,
          Type = GitHubAccountType.User,
        },
      };

      // Create an issue only to make sure its labels don't get disturbed
      // by our other edits.
      await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", issueNotToDisturb, repo.Id)));

      var redLabel = new Label() {
        Id = 2001,
        Color = "ff0000",
        Name = "red",
      };
      var blueLabel = new Label() {
        Id = 2002,
        Color = "0000ff",
        Name = "blue",
      };

      // Create an issue with some labels.  We'll edit one of the labels next.
      var githubIssue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 5,
        Labels = new[] { redLabel, blueLabel },
        User = new Account() {
          Id = user.Id,
          Login = user.Login,
          Type = GitHubAccountType.User,
        },
      };
      await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", githubIssue, repo.Id)));

      // Change "blue" label to a "green"
      var payload = new {
        action = "edited",
        label = new Label() {
          Id = blueLabel.Id,
          Color = "00ff00",
          Name = "green",
        },
        repository = new {
          id = repo.Id,
        },
      };

      var changeSummary = await ProcessEvent(CreateMessage(hook, "label", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));
      Assert.AreEqual(0, changeSummary.Organizations.Count());
      Assert.AreEqual(new long[] { repo.Id }, changeSummary.Repositories.ToArray());

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedRepo = context.Repositories.Single(x => x.Id == repo.Id);
        var labels = updatedRepo.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(3, labels.Count());
        Assert.AreEqual(blueLabel.Id, labels[0].Id);
        Assert.AreEqual("green", labels[0].Name);
        Assert.AreEqual("00ff00", labels[0].Color);
        Assert.AreEqual(purpleLabel.Id, labels[1].Id);
        Assert.AreEqual(purpleLabel.Name, labels[1].Name);
        Assert.AreEqual(purpleLabel.Color, labels[1].Color);
        Assert.AreEqual(redLabel.Id, labels[2].Id);
        Assert.AreEqual(redLabel.Name, labels[2].Name);
        Assert.AreEqual(redLabel.Color, labels[2].Color);

        var updatedIssue = context.Issues.Single(x => x.Id == issue.Id);
        var issueLabels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(2, issueLabels.Count());
        Assert.AreEqual(blueLabel.Id, issueLabels[0].Id);
        Assert.AreEqual("green", issueLabels[0].Name);
        Assert.AreEqual("00ff00", issueLabels[0].Color);
        Assert.AreEqual(redLabel.Id, issueLabels[1].Id);
        Assert.AreEqual(redLabel.Name, issueLabels[1].Name);
        Assert.AreEqual(redLabel.Color, issueLabels[1].Color);
      }
    }

    [Test]
    public async Task LabelEditedButAlreadyUpToDate() {
      Common.DataModel.User user;
      Common.DataModel.Repository repo;
      Common.DataModel.Hook hook;
      Common.DataModel.Issue issue;

      using (var context = new Common.DataModel.ShipHubContext()) {
        user = TestUtil.MakeTestUser(context);
        repo = TestUtil.MakeTestRepo(context, user.Id);
        hook = MakeTestRepoHook(context, user.Id, repo.Id);
        issue = MakeTestIssue(context, user.Id, repo.Id);
        await context.SaveChangesAsync();
      }

      var redLabel = new Label() {
        Id = 2001,
        Color = "ff0000",
        Name = "red",
      };
      var greenLabel = new Label() {
        Id = 2002,
        Color = "00ff00",
        Name = "green",
      };

      var githubIssue = new Issue() {
        Id = 1001,
        Title = "Some Title",
        Body = "Some Body",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        State = "open",
        Number = 5,
        Labels = new[] { redLabel, greenLabel },
        User = new Account() {
          Id = user.Id,
          Login = user.Login,
          Type = GitHubAccountType.User,
        },
      };
      await ProcessEvent(CreateMessage(hook, "issues", IssueChange("opened", githubIssue, repo.Id)));

      var payload = new {
        action = "edited",
        label = greenLabel,
        repository = new {
          id = repo.Id,
        },
      };

      var changeSummary = await ProcessEvent(CreateMessage(hook, "label", JObject.FromObject(payload, GitHubSerialization.JsonSerializer)));
      Assert.Null(changeSummary);

      using (var context = new Common.DataModel.ShipHubContext()) {
        var updatedRepo = context.Repositories.Single(x => x.Id == repo.Id);
        var labels = updatedRepo.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(2, labels.Count());
        Assert.AreEqual(greenLabel.Id, labels[0].Id);
        Assert.AreEqual(greenLabel.Name, labels[0].Name);
        Assert.AreEqual(greenLabel.Color, labels[0].Color);
        Assert.AreEqual(redLabel.Id, labels[1].Id);
        Assert.AreEqual(redLabel.Name, labels[1].Name);
        Assert.AreEqual(redLabel.Color, labels[1].Color);

        var updatedIssue = context.Issues.Single(x => x.Id == issue.Id);
        var issueLabels = updatedIssue.Labels.OrderBy(x => x.Name).ToArray();
        Assert.AreEqual(2, issueLabels.Count());
        Assert.AreEqual(greenLabel.Id, issueLabels[0].Id);
        Assert.AreEqual(greenLabel.Name, issueLabels[0].Name);
        Assert.AreEqual(greenLabel.Color, issueLabels[0].Color);
        Assert.AreEqual(redLabel.Id, issueLabels[1].Id);
        Assert.AreEqual(redLabel.Name, issueLabels[1].Name);
        Assert.AreEqual(redLabel.Color, issueLabels[1].Color);
      }
    }
  }
}
