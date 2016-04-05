namespace RealArtists.ShipHub.Api.Controllers {
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using System.Web.Http;

  [RoutePrefix("spider")]
  public class SpiderController : ShipHubController {
    [HttpGet]
    [Route("user/{login}")]
    public async Task<IHttpActionResult> SpiderUser(string login) {
      var user = await Context.Users
        .Include(x => x.AccessTokens)
        .SingleOrDefaultAsync(x => x.Login == login);

      if (user == null || !user.AccessTokens.Any()) {
        return Error("Cannot spider users not using ShipHub", HttpStatusCode.NotFound);
      }

      var token = user.AccessTokens.OrderBy(x => x.CreatedAt).First();

      var gh = new CachingGitHubClient(Context, user, token);
      await gh.User(login);

      return Ok();
    }

    public async Task<IHttpActionResult> SpiderUserRepositories(string login) {
      return Error("Nope");
    }
  }
}
