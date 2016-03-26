namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Threading.Tasks;
  using System.Web.Http;
  using GitHub;
  using Utilities;

  [RoutePrefix("spider")]
  public class SpiderController : ApiController {
    [HttpGet]
    [Route("user")]
    public async Task<IHttpActionResult> SpiderUser(string token) {
      var gh = GitHubSettings.CreateUserClient(token);
      var user = await gh.AuthenticatedUser();
      return Ok(user.Result);
    }
  }
}
