namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using Microsoft.AspNet.Mvc;

  [Route("api/[controller]")]
  public class TestController : Controller {
    [HttpGet]
    public string Index() {
      return "This is some text.";
    }

    [HttpGet("error")]
    public IActionResult Error() {
      throw new NotImplementedException("This method deliberately not implemented.");
    }

    [HttpGet("time")]
    public string Time() {
      return $"UTC: {DateTime.UtcNow}\nLocal: {DateTime.Now}";
    }
  }
}
