namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common;
  using Common.GitHub.Models;
  using Models;

  [RoutePrefix("spider")]
  public class SpiderController : ShipHubController {
    [HttpGet]
    [Route("users/{login}")]
    public async Task<IHttpActionResult> SpiderUser(string login) {
      var user = await Context.Users
        .Include(x => x.AccessTokens)
        .Include(x => x.MetaData)
        .SingleOrDefaultAsync(x => x.Login == login);

      if (user == null || !user.AccessTokens.Any()) {
        return Error("Cannot spider users not using ShipHub", HttpStatusCode.NotFound);
      }

      var token = user.AccessTokens.OrderBy(x => x.CreatedAt).First();

      var gh = new GitHubSpider(Context, user, token);
      await gh.RefreshUser(login);

      return Ok(Mapper.Map<ApiUser>(user));
    }

    [HttpGet]
    [Route("users/{login}/repos")]
    public async Task<IHttpActionResult> SpiderUserRepositories(string login) {
      var user = await Context.Users
        .Include(x => x.AccessTokens)
        .Include(x => x.RepositoryMetaData)
        .SingleOrDefaultAsync(x => x.Login == login);

      if (user == null || !user.AccessTokens.Any()) {
        return Error("Cannot spider users not using ShipHub", HttpStatusCode.NotFound);
      }

      var token = user.RepositoryMetaData?.AccessToken ?? user.AccessTokens.OrderBy(x => x.CreatedAt).First();

      var gh = new GitHubSpider(Context, user, token);
      var repos = await gh.RefreshRepositories();

      return Ok(Mapper.Map<IEnumerable<ApiRepository>>(repos));
    }

    [HttpGet]
    [Route("all")]
    public async Task<IHttpActionResult> SpiderAll(string token) {
      var g = GitHubSettings.CreateUserClient(token);

      var user = (await g.User()).Result;

      // Storage
      var users = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase) { { user.Login, user } };
      var orgs = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
      var repos = new Dictionary<string, Dictionary<string, Repository>>(StringComparer.OrdinalIgnoreCase);
      var assignable = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);

      // Enumerate repos
      var allRepos = (await g.Repositories()).Result;
      var withIssues = allRepos.Where(x => x.HasIssues);
      var imAssignable = withIssues.Where(x => g.Assignable(x.FullName, user.Login).Result.Result);

      foreach (var r in imAssignable) {
        repos
          .Vald(r.Owner.Login, () => new Dictionary<string, Repository>(StringComparer.OrdinalIgnoreCase))
          .Vald(r.Name, () => r);
        var ac = r.Owner.Type == GitHubAccountType.Organization ? orgs : users;
        ac.Vald(r.Owner.Login, () => r.Owner);

        foreach (var a in (await g.Assignable(r.FullName)).Result) {
          ac = a.Type == GitHubAccountType.Organization ? orgs : users;
          ac.Vald(a.Login, () => a);

          var temp = assignable
            .Vald(r.Owner.Login, () => new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase))
            .Vald(r.Name, () => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
          if (!temp.Contains(a.Login)) {
            temp.Add(a.Login);
          }
        }
      }

      return Ok(new {
        users = users.Values.OrderBy(x => x.Login),
        orgs = orgs.Values.OrderBy(x => x.Login),
        repos = repos.Values.SelectMany(x => x.Values).OrderBy(x => x.FullName),
        assignable = assignable,
      });
    }
  }
}
