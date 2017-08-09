namespace RealArtists.ShipHub.Api.Controllers {
  using System.Data.Entity;
  using System.Net;
  using System.Net.Http;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;

  [RoutePrefix("api/sync")]
  public class SettingsController : ShipHubApiController {
    private IAsyncGrainFactory _grainFactory;

    public SettingsController(IAsyncGrainFactory grainFactory) {
      _grainFactory = grainFactory;
    }

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
        return Ok(settings.SyncSettings);
      }
    }

    [HttpPut]
    [Route("settings")]
    public async Task<IHttpActionResult> SetSyncSettings([FromBody] SyncSettings syncSettings) {
      var userActor = await _grainFactory.GetGrain<IUserActor>(ShipHubUser.UserId);
      await userActor.SetSyncSettings(syncSettings);
      return StatusCode(HttpStatusCode.Accepted);
    }
  }
}

