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
      using (var context = new ShipHubContext()) {
        await context.SetAccountSettings(ShipHubUser.UserId, syncSettings);
      }

      var userActor = await _grainFactory.GetGrain<IUserActor>(ShipHubUser.UserId);
      userActor.SyncRepositories().LogFailure(ShipHubUser.DebugIdentifier);

      return StatusCode(HttpStatusCode.Accepted);
    }
  }
}

