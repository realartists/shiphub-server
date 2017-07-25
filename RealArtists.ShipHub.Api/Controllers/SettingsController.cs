namespace RealArtists.ShipHub.Api.Controllers {
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common.DataModel;
  using RealArtists.ShipHub.Common.DataModel.Types;

  [RoutePrefix("api/sync")]
  public class SettingsController : ShipHubApiController {
    private const int MaxIncludes = 100;
    private const int MaxExcludes = 100000;

    [HttpGet]
    [Route("settings")]
    public async Task<IHttpActionResult> GetSyncSettings(HttpRequestMessage request) {
      AccountSettings settings = null;

      using (var context = new ShipHubContext()) {
        settings = await context.AccountSettings
         .AsNoTracking()
         .SingleOrDefaultAsync(x => x.AccountId == ShipHubUser.UserId);
      }

      if (settings == null) {
        // Never set
        return StatusCode(HttpStatusCode.NoContent);
      } else {
        return Ok(settings);
      }
    }

    [HttpPut]
    [Route("settings")]
    public async Task<IHttpActionResult> SetSyncSettings([FromBody] SyncSettings syncSettings) {
      if (syncSettings == null
        || syncSettings.Include.Count() > MaxIncludes
        || syncSettings.Exclude.Count() > MaxExcludes) {
        return StatusCode(HttpStatusCode.InternalServerError);
      }

      using (var context = new ShipHubContext()) {
        await context.SetAccountSettings(ShipHubUser.UserId, syncSettings);
      }

      return StatusCode(HttpStatusCode.Accepted);
    }
  }
}

