namespace RealArtists.ShipHub.Api.Controllers {
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces;
  using Common;

  [RoutePrefix("orleans")]
  public class OrleansTestController : ApiController {
    private IAsyncGrainFactory _grainFactory;

    public OrleansTestController(IAsyncGrainFactory grainFactory) {
      _grainFactory = grainFactory;
    }

    [HttpGet]
    [Route("test")]
    [AllowAnonymous]
    public async Task<string> Test() {
      var echoGrain = await _grainFactory.GetGrain<IEchoActor>(0);

      return await echoGrain.Echo("Hello!");
    }
  }
}
