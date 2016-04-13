namespace RealArtists.ShipHub.Api.Controllers {
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using System.Web.Http;
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
  }
}
