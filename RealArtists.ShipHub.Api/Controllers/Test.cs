namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Web.Http;

  [AllowAnonymous]
  [RoutePrefix("api/test")]
  public class Test : ApiController {
    [HttpGet]
    [Route("")]
    public string Index() {
      return "This is some text.";
    }

    [HttpGet]
    [Route("error")]
    public IHttpActionResult Error() {
      throw new NotImplementedException("This method deliberately not implemented.");
    }

    [HttpGet]
    [Route("time")]
    public string Time() {
      return $"UTC: {DateTime.UtcNow}\nLocal: {DateTime.Now}";
    }
  }
}
