namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Reflection;
  using System.Threading.Tasks;
  using System.Web.Http;
  using ActorInterfaces;
  using ActorInterfaces.GitHub;
  using Actors.GitHub;
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.DataModel.Types;
  using Common.GitHub;
  using Orleans;

  public class LoginRequest {
    public string AccessToken { get; set; }
    public string ClientName { get; set; }
  }

  [AllowAnonymous]
  [RoutePrefix("api/authentication")]
  public class AuthenticationController : ShipHubController {
    private static readonly IGitHubHandler _handlerPipeline = new GitHubHandler();

    private IGrainFactory _grainFactory;
    private IMapper _mapper;

    private static readonly IReadOnlyList<string> _requiredOauthScopes = new List<string>() {
      "user:email",
      "repo",
      "read:org",
      "admin:repo_hook",
      "admin:org_hook",
    }.AsReadOnly();

    public AuthenticationController(IGrainFactory grainFactory, IMapper mapper) {
      _grainFactory = grainFactory;
      _mapper = mapper;
    }

    [HttpDelete]
    [Authorize]
    [Route("login")]
    public async Task<IHttpActionResult> Logout() {
      // User wants to log out.
      using (var context = new ShipHubContext()) {
        var hookDetails = await context.GetLogoutWebhooks(ShipHubUser.UserId);
        var github = _grainFactory.GetGrain<IGitHubActor>(ShipHubUser.UserId);
        var tasks = new List<Task>();

        // Delete all repo hooks where they're the only user
        tasks.AddRange(hookDetails.RepositoryHooks.Select(x => github.DeleteRepositoryWebhook(x.Name, x.HookId)));
        // Delete all org hooks where they're the only user
        tasks.AddRange(hookDetails.OrganizationHooks.Select(x => github.DeleteOrganizationWebhook(x.Name, x.HookId)));

        // Wait and log errors.
        string userInfo = $"{ShipHubUser.Login} ({ShipHubUser.UserId})";
        try {
          await Task.WhenAll(tasks);
          foreach (var task in tasks) {
            task.LogFailure(userInfo);
          }
        } catch {
          // They're logging out. We had our chance.
        }

        // TODO: Invalidate their token with GitHub

        // Invalidate their token with ShipHub
        await context.RevokeAccessToken(ShipHubUser.Token);
      }

      return Ok();
    }

    [HttpPost]
    [Route("login")]
    public async Task<IHttpActionResult> Login([FromBody] LoginRequest request) {
      if ((request?.AccessToken).IsNullOrWhiteSpace()) {
        return BadRequest($"{nameof(request.AccessToken)} is required.");
      }
      if (request.ClientName.IsNullOrWhiteSpace()) {
        return BadRequest($"{nameof(request.ClientName)} is required.");
      }

      var userClient = new LoginGitHubClient(request.AccessToken, _handlerPipeline);
      var userResponse = await userClient.User(GitHubCacheDetails.Empty);

      if (!userResponse.IsOk) {
        Error("Unable to determine account from token.", HttpStatusCode.InternalServerError, userResponse.Error);
      }

      // Check scopes
      var missingScopes = _requiredOauthScopes.Except(userResponse.Scopes).ToArray();
      if (missingScopes.Any()) {
        return Error("Insufficient scopes granted.", HttpStatusCode.Unauthorized, new {
          Granted = userResponse.Scopes,
          Missing = missingScopes,
        });
      }

      var userInfo = userResponse.Result;

      if (userInfo.Type != Common.GitHub.Models.GitHubAccountType.User) {
        Error("Token must be for a user.", HttpStatusCode.BadRequest);
      }

      using (var context = new ShipHubContext()) {
        // Create account using stored procedure
        // This ensures it exists (simpler logic) and also won't collide with sync.
        await context.BulkUpdateAccounts(userResponse.Date, new[] { _mapper.Map<AccountTableType>(userInfo) });

        // Save user access details
        await context.SetUserAccessToken(userInfo.Id, string.Join(",", userResponse.Scopes), userResponse.RateLimit);
      }

      var userGrain = _grainFactory.GetGrain<IUserActor>(userInfo.Id);
      userGrain.Sync().LogFailure($"{userInfo.Login} ({userInfo.Id})");

      return Ok(userInfo);
    }
  }
}
