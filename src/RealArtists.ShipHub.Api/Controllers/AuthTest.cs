namespace RealArtists.ShipHub.Api.Controllers {
  using Microsoft.AspNetCore.Mvc;

  [Route("api/[controller]")]
  public class AuthTest : Controller {
    public string Index() {
      return $"Hello {User.ToString()}";
    }
  }
}
