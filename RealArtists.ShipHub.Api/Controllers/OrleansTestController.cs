namespace RealArtists.ShipHub.Api.Controllers {
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces;
  using Orleans;

  [RoutePrefix("orleans")]
  public class OrleansTestController : ApiController {
    private IGrainFactory _grainFactory;

    public OrleansTestController(IGrainFactory grainFactory) {
      _grainFactory = grainFactory;
    }

    [HttpGet]
    [Route("test")]
    [AllowAnonymous]
    public Task<string> Test() {
      var echoGrain = _grainFactory.GetGrain<IEchoActor>(0);

      return echoGrain.Echo("Hello!");
    }
  }
}
