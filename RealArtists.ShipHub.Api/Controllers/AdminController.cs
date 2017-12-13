using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using RealArtists.ShipHub.ActorInterfaces;
using RealArtists.ShipHub.ActorInterfaces.GitHub;
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
        return new HttpResponseMessage(HttpStatusCode.Forbidden);
      }

      long? repoId = null;
      using (var context = new ShipHubContext()) {
        var repoFullName = $"{owner}/{repo}";
        repoId = (await context.Repositories.SingleOrDefaultAsync(r => r.FullName == repoFullName))?.Id;
      }

      if (repoId == null) {
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      }

      var repoActor = await _grainFactory.GetGrain<IRepositoryActor>(repoId.Value);
      repoActor.ForceResyncRepositoryIssues().LogFailure();

      return new HttpResponseMessage(HttpStatusCode.OK);
    }

    [AllowAnonymous]
    [HttpPut]
    [Route("collecthooks")]
    public async Task<HttpResponseMessage> CollectHooks() {
      // first, validate that the secret is presented
      Request.Headers.TryGetValues("X-Admin-Secret", out var presentedSecrets);
      var presentedSecret = presentedSecrets?.FirstOrDefault();
      var secret = _configuration.AdminSecret;

      if (secret.IsNullOrWhiteSpace() || presentedSecret != secret) {
        return new HttpResponseMessage(HttpStatusCode.Forbidden);
      }

      var githubDeleted = 0;
      var toDelete = new List<long>();

      using (var context = new ShipHubContext()) {
        var excess = await context.ExcessHooks();
        var grouped = excess
          .GroupBy(x => x.Id)
          .ToDictionary(x => x.Key, x => x);

        foreach (var hook in grouped) {
          // Try each admin until they all fail or we succeed.
          foreach (var record in hook.Value) {
            var admin = await _grainFactory.GetGrain<IGitHubActor>(record.AccountId);
            var delete = await admin.DeleteRepositoryWebhook(record.RepoFullName, record.Id);
            try {
              if (delete.Succeeded) {
                ++githubDeleted;
                break; // Inner foreach
              }
            } catch (Exception e) {
              e.Report($"Error collecting hook {record.Id} on {record.RepoFullName} using {record.AccountId}");
            }
          }
        }

        var processedHooks = grouped.Keys.ToArray();
        await context.BulkUpdateHooks(deleted: processedHooks);

        return new HttpResponseMessage(HttpStatusCode.OK) {
          Content = new StringContent(new { RemovedFromGitHub = githubDeleted, RemovedFromShip = processedHooks.Length }.SerializeObject()),
        };
      }
    }
  }
}
