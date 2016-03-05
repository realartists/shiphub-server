namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using System.Web.Http;
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

    [HttpPost]
    [Route("hello")]
    public async Task<IHttpActionResult> Login(string applicationId, string code, string state, string clientName) {
      if (string.IsNullOrWhiteSpace(applicationId)) {
        return BadRequest($"{nameof(applicationId)} is required.");
      }
      if (string.IsNullOrWhiteSpace(code)) {
        return BadRequest($"{nameof(code)} is required.");
      }
      if (string.IsNullOrWhiteSpace(state)) {
        return BadRequest($"{nameof(state)} is required.");
      }
      if (string.IsNullOrWhiteSpace(clientName)) {
        return BadRequest($"{nameof(clientName)} is required.");
      }

      if (!GitHubSettings.Credentials.ContainsKey(applicationId)) {
        return Error($"Unknown applicationId: {applicationId}", HttpStatusCode.NotFound);
      }
      var secret = GitHubSettings.Credentials[applicationId];

      var appClient = GitHubSettings.CreateApplicationClient(applicationId);
      var tokenInfo = await appClient.CreateAccessToken(applicationId, secret, code, state);
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
        account.ETag = userInfo.ETag;
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
        context.CreateAuthenticationToken(shipUser, clientName);

        await context.SaveChangesAsync();
      }

      return Ok();
    }
  }
}
