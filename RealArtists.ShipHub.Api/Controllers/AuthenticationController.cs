namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using System.Web.Http;
  using DataModel;
  using Models;
  using Utilities;

  [AllowAnonymous]
  [RoutePrefix("api/authentication")]
  public class AuthenticationController : ShipHubController {
    private static readonly IReadOnlyList<string> _requiredOauthScopes = new List<string>() {
      "user:email",
      "repo",
      "admin:repo_hook",
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
      var scope = string.Join(",", _requiredOauthScopes);
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

      return await Code(new CodeRequest() {
        ApplicationId = clientId,
        ClientName = "Loopback",
        Code = code,
        State = state
      });
    }

    public class CodeRequest {
      public string ApplicationId { get; set; }
      public string Code { get; set; }
      public string State { get; set; }
      public string ClientName { get; set; }
    }

    [HttpPost]
    [Route("code")]
    public async Task<IHttpActionResult> Code([FromBody] CodeRequest request) {
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

      return await Hello(new HelloRequest() {
        AccessToken = appAuth.AccessToken,
        ApplicationId = request.ApplicationId,
        ClientName = request.ClientName,
      });
    }

    public class HelloRequest {
      public string AccessToken { get; set; }
      public string ApplicationId { get; set; }
      public string ClientName { get; set; }
    }

    [HttpPost]
    [Route("hello")]
    public async Task<IHttpActionResult> Hello([FromBody] HelloRequest request) {
      if (string.IsNullOrWhiteSpace(request.ApplicationId)) {
        return BadRequest($"{nameof(request.ApplicationId)} is required.");
      }
      if (string.IsNullOrWhiteSpace(request.AccessToken)) {
        return BadRequest($"{nameof(request.AccessToken)} is required.");
      }
      if (string.IsNullOrWhiteSpace(request.ClientName)) {
        return BadRequest($"{nameof(request.ClientName)} is required.");
      }

      if (!GitHubSettings.Credentials.ContainsKey(request.ApplicationId)) {
        return Error($"Unknown applicationId: {request.ApplicationId}", HttpStatusCode.NotFound);
      }

      // Check scopes
      var appClient = GitHubSettings.CreateApplicationClient(request.ApplicationId);
      var authRequest = await appClient.CheckAccessToken(request.ApplicationId, request.AccessToken);
      if (authRequest.Error != null) {
        return Error("Token validation failed.", HttpStatusCode.Unauthorized, authRequest.Error);
      }
      var authInfo = authRequest.Result;

      var missingScopes = _requiredOauthScopes.Except(authInfo.Scopes).ToArray();
      if (missingScopes.Any()) {
        return Error("Insufficient access granted.", HttpStatusCode.Unauthorized, new {
          MissingScopes = missingScopes,
        });
      }

      var userClient = GitHubSettings.CreateUserClient(authInfo.Token);
      var userInfo = await userClient.AuthenticatedUser();
      if (userInfo.Error != null) {
        return Error("Unable to retrieve user information.", HttpStatusCode.Unauthorized, userInfo.Error);
      }
      var ghId = userInfo.Result.Id;

      using (var context = new ShipHubContext()) {
        // GitHub Setup
        var account = await context.Accounts
          .Include(x => x.AccessToken)
          .SingleOrDefaultAsync(x => x.Id == ghId);
        if (account == null) {
          account = context.Accounts.Add(context.Accounts.Create());
          account.Id = ghId;
        }
        account.Update(userInfo);

        if (account.AccessToken == null) {
          account.AccessToken = context.AccessTokens.Add(new AccessToken() {
            Account = account,
          });
        }
        var accessToken = account.AccessToken;
        accessToken.AccessToken = authInfo.Token;
        accessToken.ApplicationId = request.ApplicationId;
        accessToken.Scopes = string.Join(",", authInfo.Scopes);
        accessToken.UpdateRateLimits(userInfo);

        // ShipHub Setup
        var shipUser = await context.Users
          .Include(x => x.AuthenticationTokens)
          .SingleOrDefaultAsync(x => x.GitHubAccountId == account.Id);
        if (shipUser == null) {
          shipUser = context.Users.Add(context.Users.Create());
          shipUser.GitHubAccount = account;
        }
        var shipToken = context.CreateAuthenticationToken(shipUser, request.ClientName);

        await context.SaveChangesAsync();

        return Ok(new {
          Session = shipToken.Id,
          User = new ApiUser() {
            AvatarUrl = account.AvatarUrl,
            Company = account.Company,
            Identifier = shipUser.Id,
            GitHubId = account.Id,
            Login = account.Login,
            Name = account.Name,
          }
        });
      }
    }
  }
}
