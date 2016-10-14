namespace RealArtists.ShipHub.Api.Controllers {
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces;
  using global::Orleans;
  using Orleans;

  [RoutePrefix("orleans")]
  public class OrleansTestController : ApiController {
    [HttpGet]
    [Route("test")]
    [AllowAnonymous]
    public Task<string> Test() {
      if (!OrleansAppServiceClient.IsInitialized) {
        var config = OrleansAppServiceClient.DefaultConfiguration();
        OrleansAppServiceClient.Initialize(config);
      }

      var echoGrain = GrainClient.GrainFactory.GetGrain<IEchoActor>(0);

      return echoGrain.Echo("Hello!");
    }
  }
}
