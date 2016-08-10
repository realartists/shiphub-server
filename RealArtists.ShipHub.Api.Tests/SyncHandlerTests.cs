namespace RealArtists.ShipHub.Api.Tests {
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Moq;
  using RealArtists.ShipHub.QueueProcessor;
  using Xunit;
  using RealArtists.ShipHub.Common.GitHub;
  using RealArtists.ShipHub.Common.GitHub.Models;
  using RealArtists.ShipHub.QueueClient.Messages;

  public class SyncHandlerTests {
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

