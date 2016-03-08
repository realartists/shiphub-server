namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using System.Web.Http;
  using System.Web.Http.Cors;
  using DataModel;
  using Utilities;

  [AllowAnonymous]
  [RoutePrefix("api/authentication")]
  public class AuthenticationController : ShipHubController {
    private static readonly IReadOnlyList<string> _requiredOauthScopes = new List<string>() {
      "notifications",
      "repo",
      "admin:repo_hook",
    }.AsReadOnly();

    private static readonly IReadOnlyList<string> _optionalOauthScopes = new List<string>() {
      "read:org",
      "admin:org_hook",
    }.AsReadOnly();

    [HttpGet]
    [Route("begin")]
    public IHttpActionResult Begin(string clientId = "dc74f7ec664b73a51971") {
      if (!GitHubSettings.Credentials.ContainsKey(clientId)) {
        return Error($"Unknown applicationId: {clientId}", HttpStatusCode.NotFound);
      }
      var secret = GitHubSettings.Credentials[clientId];

      var scope = string.Join(",", _requiredOauthScopes.Union(_optionalOauthScopes).ToArray());

      var redir = new Uri(Request.RequestUri, Url.Link("callback", new { clientId = clientId })).ToString();

      string uri = $"https://github.com/login/oauth/authorize?client_id={clientId}&scope={scope}&redirect_uri={WebUtility.UrlEncode(redir)}";

      return Redirect(uri);
    }

    [HttpGet]
    [Route("end", Name = "callback")]
    public async Task<IHttpActionResult> End(string clientId, string code, string state = null) {
      if (!GitHubSettings.Credentials.ContainsKey(clientId)) {
        return Error($"Unknown applicationId: {clientId}", HttpStatusCode.NotFound);
      }

      return await Hello(new HelloRequest() {
        ApplicationId = clientId,
        ClientName = "Loopback",
        Code = code,
        State = state
      });
    }

    public class HelloRequest {
      public string ApplicationId { get; set; }
      public string Code { get; set; }
      public string State { get; set; }
      public string ClientName { get; set; }
    }

    [HttpPost]
    [Route("hello")]
    [EnableCors("*", "*", "*")]
    public async Task<IHttpActionResult> Hello([FromBody] HelloRequest request) {
      if (string.IsNullOrWhiteSpace(request.ApplicationId)) {
        return BadRequest($"{nameof(request.ApplicationId)} is required.");
      }
      if (string.IsNullOrWhiteSpace(request.Code)) {
        return BadRequest($"{nameof(request.Code)} is required.");
      }
      if (string.IsNullOrWhiteSpace(request.ClientName)) {
        return BadRequest($"{nameof(request.ClientName)} is required.");
      }

      if (!GitHubSettings.Credentials.ContainsKey(request.ApplicationId)) {
        return Error($"Unknown applicationId: {request.ApplicationId}", HttpStatusCode.NotFound);
      }
      var secret = GitHubSettings.Credentials[request.ApplicationId];

      var appClient = GitHubSettings.CreateClient();
      var tokenInfo = await appClient.CreateAccessToken(request.ApplicationId, secret, request.Code, request.State);
      if (tokenInfo.Error != null) {
        return Error("Unable to retrieve token.", tokenInfo.Status, tokenInfo.Error);
      }
      var appAuth = tokenInfo.Result;

      // Check scopes
      var scopes = appAuth.Scope.Split(',');
      var missingScopes = _requiredOauthScopes.Except(scopes).ToArray();
      if (missingScopes.Any()) {
        return Error("Insufficient access granted.", HttpStatusCode.Unauthorized, new {
          MissingScopes = missingScopes,
        });
      }

      var userClient = GitHubSettings.CreateUserClient(appAuth.AccessToken);
      var userInfo = await userClient.AuthenticatedUser();
      if (userInfo.Error != null) {
        return Error("Unable to retrieve user information.", HttpStatusCode.Unauthorized, userInfo.Error);
      }
      var user = userInfo.Result;

      using (var context = new ShipHubContext()) {
        // GitHub Setup
        var account = await context.Accounts
          .Include(x => x.AccessToken)
          .SingleOrDefaultAsync(x => x.Id == user.Id);
        if (account == null) {
          account = context.Accounts.Create();
          account.Id = user.Id;
        }
        account.AvatarUrl = user.AvatarUrl;
        account.Company = user.Company ?? "";
        account.CreatedAt = user.CreatedAt;
        account.Login = user.Login;
        account.Name = user.Name;
        account.UpdatedAt = user.UpdatedAt;
        account.ETag = userInfo.ETag;
        account.Expires = userInfo.Expires;
        account.LastModified = userInfo.LastModified;
        account.LastRefresh = DateTimeOffset.UtcNow;

        if (account.AccessToken == null) {
          account.AccessToken = context.AccessTokens.Add(new GitHubAccessTokenModel() {
            Account = account,
          });
        }
        var accessToken = account.AccessToken;
        accessToken.AccessToken = appAuth.AccessToken;
        accessToken.Scopes = appAuth.Scope;
        accessToken.UpdateRateLimits(userInfo);

        // ShipHub Setup
        var shipUser = await context.Users
          .Include(x => x.AuthenticationTokens)
          .SingleOrDefaultAsync(x => x.GitHubAccountId == user.Id);
        if (shipUser == null) {
          shipUser = context.Users.Create();
          shipUser.GitHubAccount = account;
        }
        context.CreateAuthenticationToken(shipUser, request.ClientName);

        await context.SaveChangesAsync();
      }

      return Ok();
    }
  }
}
