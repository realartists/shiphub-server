namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Threading.Tasks;
  using Configuration;
  using DataModel;
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.Extensions.Options;

  [AllowAnonymous]
  [Route("api/[controller]")]
  public class Authentication : ShipHubController {
    private GitHubOptions _ghOpts;
    private GitHubContext _ghContext;

    private static readonly IReadOnlyList<string> _requiredOauthScopes = new List<string>() {
      "notifications",
      "repo",
      "admin:repo_hook",
    }.AsReadOnly();

    private static readonly IReadOnlyList<string> _optionalOauthScopes = new List<string>() {
      "read:org",
      "admin:org_hook",
    }.AsReadOnly();

    public Authentication(IOptions<GitHubOptions> ghOpts, GitHubContext ghContext) {
      _ghOpts = ghOpts.Value;
      _ghContext = ghContext;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(string applicationId, string code, string state, string clientName) {
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

      var creds = _ghOpts.Credentials.SingleOrDefault(x => x.Id.Equals(applicationId, StringComparison.OrdinalIgnoreCase));
      if (creds == null) {
        return Error($"Unknown applicationId: {applicationId}", HttpStatusCode.NotFound);
      }

      var appClient = _ghOpts.CreateApplicationClient(creds.Id);
      var tokenInfo = await appClient.CreateAccessToken(creds.Id, creds.Secret, code, state);
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

      var userClient = _ghOpts.CreateUserClient(appAuth.AccessToken);
      var userInfo = await userClient.AuthenticatedUser();
      if (userInfo.Error != null) {
        return Error("Unable to retrieve user information.", HttpStatusCode.Unauthorized, userInfo.Error);
      }
      var user = userInfo.Result;

      var account = await _ghContext.Accounts
        .Include(x => x.AuthenticationToken)
        .SingleOrDefaultAsync(x => x.Id == user.Id);
      if (account == null) {
        account = _ghContext.Accounts.Create();
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

      if (account.AuthenticationToken == null) {
        account.AuthenticationToken = _ghContext.AuthenticationTokens.Add(new GitHubAuthenticationTokenModel() {
          Account = account,
        });
      }
      account.AuthenticationToken.AccessToken = appAuth.AccessToken;
      account.AuthenticationToken.Scopes = appAuth.Scope;

      await _ghContext.SaveChangesAsync();

      return Ok();
    }
  }
}
