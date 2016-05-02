namespace ConsoleSpider {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using RealArtists.Ship.Server.QueueClient;
  using RealArtists.ShipHub.Common;
  using RealArtists.ShipHub.Common.GitHub;
  using RealArtists.ShipHub.Common.GitHub.Models;

  public class SpiderSession {
    GitHubClient _g;
    bool _hasRun = false;
    ResourceUpdateClient queue = new ResourceUpdateClient();

    public SpiderSession(string accessToken) {
      _g = GitHubSettings.CreateUserClient(accessToken);
    }

    public async Task Run() {
      if (_hasRun) {
        throw new InvalidOperationException("Only once.");
      }
      _hasRun = true;

      var user = (await _g.User()).Result;
      await queue.Send(user);

      // repos
      var repos = (await _g.Repositories()).Result
        .Where(x => x.HasIssues)
        .Where(x => _g.Assignable(x.FullName, user.Login).Result.Result);
      foreach (var repo in repos) {
        await queue.Send(repo.Owner);
        await queue.Send(repo);

        var assignable = (await _g.Assignable(repo.FullName)).Result;
        var tasks = assignable.Select(x => queue.Send(x));
        await Task.WhenAll(tasks);

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
