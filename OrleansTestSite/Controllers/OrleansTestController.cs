namespace OrleansTestSite.Controllers {
  using System.Threading.Tasks;
  using System.Web.Http;
  using global::Orleans;
  using Orleans;
  using RealArtists.ShipHub.ActorInterfaces;

  [RoutePrefix("orleans")]
  public class OrleansTestController : ApiController {
    [HttpGet]
    [Route("test")]
    [AllowAnonymous]
    public Task<string> Test() {
      //if (!OrleansAppServiceClient.IsInitialized) {
      //  var config = OrleansAppServiceClient.DefaultConfiguration();
      //  OrleansAppServiceClient.Initialize(config);
      //}
      if (!GrainClient.IsInitialized) {
        GrainClient.Initialize(OrleansAppServiceClient.DefaultConfiguration());
      }

      var echoGrain = GrainClient.GrainFactory.GetGrain<IEchoActor>(0);

      return echoGrain.Echo("Hello!");
    }

    [HttpGet]
    [Route("test2")]
    [AllowAnonymous]
    public string Test2() {
      return "Success!";
    }
  }
}
