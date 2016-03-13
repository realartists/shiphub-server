namespace RealArtists.ShipHub.Api.Controllers {
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web.Http;
  using DataModel;
  using Utilities;

  [RoutePrefix("etest")]
  public class EntityTestController : ApiController {
    private ShipHubContext _context = new ShipHubContext();

    [HttpGet]
    [Route("user")]
    public async Task<IHttpActionResult> TestUser() {
      var account = _context.Accounts.Add(new User() {
        Id = 1,
        Login = "test",
        Name = "EF Test",
      });
      await _context.SaveChangesAsync();

      _context.Accounts.Remove(account);
      await _context.SaveChangesAsync();

      return Ok("Ok");
    }

    [HttpGet]
    [Route("accessToken")]
    public async Task<IHttpActionResult> AccessToken() {
      var account = _context.Accounts.Add(new User() {
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
      account.AccessToken = _context.AccessTokens.Add(token);
      await _context.SaveChangesAsync();

      _context.Accounts.Remove(account);
      await _context.SaveChangesAsync();

      return Ok("Ok");
    }

    [HttpGet]
    [Route("authToken")]
    public async Task<IHttpActionResult> AuthenticationToken() {
      var account = _context.Accounts.Add(new User() {
        Id = 1,
        Login = "test",
        Name = "EF Test",
      });
      var token2 = _context.CreateAuthenticationToken(account, "efTest");
      await _context.SaveChangesAsync();

      _context.Accounts.Remove(account);
      await _context.SaveChangesAsync();

      return Ok("Ok");
    }

    protected override void Dispose(bool disposing) {
      if (disposing) {
        var temp = Interlocked.Exchange(ref _context, null);
        if (temp != null) {
          temp.Dispose();
        }
      }
      base.Dispose(disposing);
    }
  }
}