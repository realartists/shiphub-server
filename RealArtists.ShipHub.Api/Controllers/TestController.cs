namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.Net;
  using System.Net.Http;
  using System.Net.Mime;
  using System.Text;
  using System.Web.Http;

  [AllowAnonymous]
  [RoutePrefix("test")]
  public class TestController : ApiController {
    [HttpGet]
    [Route("")]
    public string Index() {
      return "This is some text.";
    }

    [HttpGet]
    [Route("auth")]
    public IHttpActionResult Authentication() {
      return Json(User);
    }

    [HttpGet]
    [Route("error")]
    public IHttpActionResult Error() {
      throw new NotImplementedException("This method deliberately not implemented.");
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    [HttpGet]
    [Route("time")]
    public HttpResponseMessage Time() {
      return new HttpResponseMessage(HttpStatusCode.OK) {
        Content = new StringContent($"UTC: {DateTime.UtcNow}\nLocal: {DateTime.Now}", Encoding.UTF8, MediaTypeNames.Text.Plain),
      };
    }
  }
}
