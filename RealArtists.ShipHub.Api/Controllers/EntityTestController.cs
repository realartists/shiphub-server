namespace RealArtists.ShipHub.Api.Controllers {
  using System.Threading.Tasks;
  using System.Web.Http;
  using DataModel;
  using Utilities;

  [RoutePrefix("etest")]
  public class EntityTestController : ShipHubController {
    [HttpGet]
    [Route("user")]
    public async Task<IHttpActionResult> TestUser() {
      var account = Context.Accounts.Add(new User() {
        Id = 1,
        Login = "test",
        Name = "EF Test",
      });
      await Context.SaveChangesAsync();

      Context.Accounts.Remove(account);
      await Context.SaveChangesAsync();

      return Ok("Ok");
    }

    [HttpGet]
    [Route("accessToken")]
    public async Task<IHttpActionResult> AccessToken() {
      var account = Context.Accounts.Add(new User() {
        Id = 1,
        Login = "test",
        Name = "EF Test",
      });
      var token = new AccessToken() {
        Account = account,
        ApplicationId = "efTest",
        Scopes = "testing,more.testing",
        Token = "thingsAndStuffAndThins"
      };
      account.PrimaryToken = Context.AccessTokens.Add(token);
      await Context.SaveChangesAsync();

      Context.Accounts.Remove(account);
      await Context.SaveChangesAsync();

      return Ok("Ok");
    }

    [HttpGet]
    [Route("authToken")]
    public async Task<IHttpActionResult> AuthenticationToken() {
      var account = (User)Context.Accounts.Add(new User() {
        Id = 1,
        Login = "test",
        Name = "EF Test",
      });
      var token2 = Context.CreateAuthenticationToken(account, "efTest");
      await Context.SaveChangesAsync();

      Context.Accounts.Remove(account);
      await Context.SaveChangesAsync();

      return Ok("Ok");
    }
  }
}