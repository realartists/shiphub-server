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
  using AutoMapper;
  using Common;
  using Common.DataModel;
  using Common.GitHub;
  using Orleans;
  using RealArtists.ShipHub.Common.DataModel.Types;

  public class LoginRequest {
    public string AccessToken { get; set; }
    public string ClientName { get; set; }
  }

  [AllowAnonymous]
  [RoutePrefix("api/authentication")]
  public class AuthenticationController : ShipHubController {
    private static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    private static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

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

    private static GitHubClient CreateGitHubClient(string accessToken) {
      return new GitHubClient(ShipHubCloudConfiguration.Instance.GitHubApiRoot, _handlerPipeline, ApplicationName, ApplicationVersion, "ShipHub Authentication Controller", Guid.NewGuid(), GitHubClient.InvalidUserId, accessToken);
    }

    public AuthenticationController(IGrainFactory grainFactory, IMapper mapper) {
      _grainFactory = grainFactory;
      _mapper = mapper;
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

      var userClient = CreateGitHubClient(request.AccessToken);
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

      // Create account using stored procedure
      // This ensures it exists (simpler logic) and also won't collide with sync.
      await Context.BulkUpdateAccounts(userResponse.Date, new[] { _mapper.Map<AccountTableType>(userInfo) });

      // There's no concurrency check on accounts, so the rest of this is safe.
      var user = await Context.Users.SingleAsync(x => x.Id == userInfo.Id);
      user.Token = userResponse.CacheData.AccessToken;
      user.Scopes = string.Join(",", userResponse.Scopes);
      await Context.SaveChangesAsync();

      await Context.UpdateRateLimit(userResponse.RateLimit);

      var userGrain = _grainFactory.GetGrain<IUserActor>(user.Id);
      userGrain.Sync().LogFailure($"{user.Login} ({user.Id})");

      return Ok(userInfo);
    }
  }
}
