using System.Data.Entity;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using RealArtists.ShipHub.ActorInterfaces;
using RealArtists.ShipHub.Common;
using RealArtists.ShipHub.Common.DataModel;

namespace RealArtists.ShipHub.Api.Controllers {
  [RoutePrefix("admin")]
  public class AdminController : ApiController {
    private IShipHubConfiguration _configuration;
    private IAsyncGrainFactory _grainFactory;
    public AdminController(IShipHubConfiguration config, IAsyncGrainFactory grainFactory) {
      _configuration = config;
      _grainFactory = grainFactory;
    }

    [AllowAnonymous]
    [HttpPut]
    [Route("repo/{owner}/{repo}/issues/resync")]
    public async Task<HttpResponseMessage> Resync(string owner, string repo) {
      // first, validate that the secret is presented
      Request.Headers.TryGetValues("X-Admin-Secret", out var presentedSecrets);
      var presentedSecret = presentedSecrets?.FirstOrDefault();
      var secret = _configuration.AdminSecret;
      
      if (secret.IsNullOrWhiteSpace() || presentedSecret != secret) {
        return new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
      }

      long? repoId = null;
      using (var context = new ShipHubContext()) {
        var repoFullName = $"{owner}/{repo}";
        repoId = (await context.Repositories.SingleOrDefaultAsync(r => r.FullName == repoFullName))?.Id;
      }

      if (repoId == null) {
        return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
      }

      var repoActor = await _grainFactory.GetGrain<IRepositoryActor>(repoId.Value);
      repoActor.ForceResyncRepositoryIssues().LogFailure();

      return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
    }
  }
}