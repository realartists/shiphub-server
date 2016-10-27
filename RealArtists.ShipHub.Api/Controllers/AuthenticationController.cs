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

  public class HelloRequest {
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
      return new GitHubClient(_handlerPipeline, ApplicationName, ApplicationVersion, "ShipHub Authentication Controller", Guid.NewGuid(), accessToken, null);
    }

    public AuthenticationController(IGrainFactory grainFactory, IMapper mapper) {
      _grainFactory = grainFactory;
      _mapper = mapper;
    }

    [HttpPost]
    [Route("login")]
    public async Task<IHttpActionResult> Hello([FromBody] HelloRequest request) {
      if (request.AccessToken.IsNullOrWhiteSpace()) {
        return BadRequest($"{nameof(request.AccessToken)} is required.");
      }
      if (request.ClientName.IsNullOrWhiteSpace()) {
        return BadRequest($"{nameof(request.ClientName)} is required.");
      }

      var userClient = CreateGitHubClient(request.AccessToken);
      var userResponse = await userClient.User(GitHubCacheDetails.Empty);

      if (userResponse.IsError) {
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

      var user = await Context.Users
        .SingleOrDefaultAsync(x => x.Id == userInfo.Id);
      if (user == null) {
        user = (User)Context.Accounts.Add(new User() {
          Id = userInfo.Id,
        });
      }
      _mapper.Map(userInfo, user);
      user.Token = userResponse.CacheData.AccessToken;
      user.Scopes = string.Join(",", userResponse.Scopes);
      user.RateLimit = userResponse.RateLimit.RateLimit;
      user.RateLimitRemaining = userResponse.RateLimit.RateLimitRemaining;
      user.RateLimitReset = userResponse.RateLimit.RateLimitReset;

      // Be sure to save the account *before* syncing it!
      await Context.SaveChangesAsync();

      var userGrain = _grainFactory.GetGrain<IUserActor>(user.Id);
      await userGrain.UpdateToken(user.Token); // Just in case it has changed since the grain was loaded.
      await userGrain.Sync();

      return Ok(userInfo);
    }
  }
}
