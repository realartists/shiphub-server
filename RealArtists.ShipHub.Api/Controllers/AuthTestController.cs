namespace RealArtists.ShipHub.Api.Controllers {
  using System.Web.Http;

  [RoutePrefix("api/authTest")]
  public class AuthTestController : ApiController {
    [HttpGet]
    [Route("")]
    public string Index() {
      return $"Hello {User.ToString()}";
    }
  }
}
