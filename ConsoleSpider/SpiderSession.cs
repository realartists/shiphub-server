namespace ConsoleSpider {
  using System;
  using System.Linq;
  using System.Threading.Tasks;
  using RealArtists.Ship.Server.QueueClient;
  using RealArtists.ShipHub.Common;
  using RealArtists.ShipHub.Common.GitHub;

  public class SpiderSession {
    GitHubClient _g;
    bool _hasRun = false;
    ShipHubQueueClient queue = new ShipHubQueueClient();

    public SpiderSession(string accessToken) {
      _g = GitHubSettings.CreateUserClient(accessToken);
    }

    public async Task Run() {
      if (_hasRun) {
        throw new InvalidOperationException("Only once.");
      }
      _hasRun = true;

      var userResponse = await _g.User();
      var user = userResponse.Result;
      await queue.UpdateAccount(user, userResponse.Date);

      // repos
      var repoResponse = await _g.Repositories();
      var repos = repoResponse.Result
        .Where(x => x.HasIssues)
        .Where(x => _g.Assignable(x.FullName, user.Login).Result.Result);
      foreach (var repo in repos) {
        await queue.UpdateRepository(repo, repoResponse.Date);

        var assignableResponse = await _g.Assignable(repo.FullName);
        var assignable = assignableResponse.Result;

        var tasks = assignable.Select(x => queue.UpdateAccount(x, assignableResponse.Date));
        await Task.WhenAll(tasks);

        await queue.UpdateRepositoryAssignable(repo, assignable, assignableResponse.Date);

        // enumerate issues
        // TODO: Store them
        //var issues = (await _g.Issues(repo.FullName)).Result;
        //foreach (var i in issues) {
        //  await queue.Send(i.Assignee);
        //  await queue.Send(i.ClosedBy);
        //  await queue.Send(i.User);

        //}

        // enumerate comments

      }
    }
  }
}
