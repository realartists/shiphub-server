namespace RealArtists.ShipHub.Api.Controllers {
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using System.Web.Http;
  using Common;
  using Common.DataModel;
  using QueueClient;

  public class CodeRequest {
    public string ApplicationId { get; set; }
    public string Code { get; set; }
    public string State { get; set; }
    public string ClientName { get; set; }
  }

  public class HelloRequest {
    public string AccessToken { get; set; }
    public string ClientName { get; set; }
  }

  [AllowAnonymous]
  [RoutePrefix("api/authentication")]
  public class AuthenticationController : ShipHubController {
    private static readonly ShipHubBusClient _QueueClient = new ShipHubBusClient();

    private static readonly IReadOnlyList<string> _requiredOauthScopes = new List<string>() {
      "user:email",
      "repo",
      "read:org",
    }.AsReadOnly();

    [HttpPost]
    [Route("login")]
    public async Task<IHttpActionResult> Hello([FromBody] HelloRequest request) {
      if (string.IsNullOrWhiteSpace(request.AccessToken)) {
        return BadRequest($"{nameof(request.AccessToken)} is required.");
      }
      if (string.IsNullOrWhiteSpace(request.ClientName)) {
        return BadRequest($"{nameof(request.ClientName)} is required.");
      }

      var userClient = GitHubSettings.CreateUserClient(request.AccessToken);
      // DO NOT SEND ANY OPTIONS - we want to ensure we use the default credentials.
      var userResponse = await userClient.User();

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
        .Include(x => x.Organizations)
        .SingleOrDefaultAsync(x => x.Id == userInfo.Id);
      if (user == null) {
        user = (User)Context.Accounts.Add(new User() {
          Id = userInfo.Id,
        });
      }
      Mapper.Map(userInfo, user);
      user.Token = userResponse.Credentials.Parameter;
      user.Scopes = string.Join(",", userResponse.Scopes);
      user.RateLimit = userResponse.RateLimit.RateLimit;
      user.RateLimitRemaining = userResponse.RateLimit.RateLimitRemaining;
      user.RateLimitReset = userResponse.RateLimit.RateLimitReset;

      await Context.SaveChangesAsync();

      await _QueueClient.SyncAccount(user.Token);

      return Ok(userInfo);
    }
  }
}
