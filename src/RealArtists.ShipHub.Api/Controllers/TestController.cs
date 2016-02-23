namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Microsoft.AspNet.Mvc;

  [Route("api/[controller]")]
  public class TestController : Controller {
    [HttpGet]
    public IActionResult Get() {
      //return new string[] { "value1", "value2" };
      return Ok();
    }

    [HttpGet("{id}")]
    public string Get(int id) {
      return "value";
    }

    [HttpPost]
    public void Post([FromBody]string value) {
    }

    [HttpPut("{id}")]
    public void Put(int id, [FromBody]string value) {
    }

    [HttpDelete("{id}")]
    public void Delete(int id) {
    }
  }
}
