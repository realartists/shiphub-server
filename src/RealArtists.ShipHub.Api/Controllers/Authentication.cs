namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;
  using Configuration;
  using DataModel;
  using Microsoft.AspNetCore.Authorization;
  using Microsoft.AspNetCore.Mvc;
  using Microsoft.Extensions.Options;
  using Octokit;

  [AllowAnonymous]
  [Route("api/[controller]")]
  public class Authentication : Controller {
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
    public async Task<IActionResult> Login([FromBody] string accessToken) {
      if (string.IsNullOrWhiteSpace(accessToken)) {
        return BadRequest($"{nameof(accessToken)} is required.");
      }

      try {
        var appClient = _ghOpts.CreateApplicationClient();
        var appAuth = await appClient.Authorization.CheckApplicationAuthentication(_ghOpts.ApplicationId, accessToken);

        // Check scopes
        var missingScopes = _requiredOauthScopes.Except(appAuth.Scopes).ToArray();
        if (missingScopes.Any()) {
          return Unauthorized();
        }

        var userClient = _ghOpts.CreateUserClient(appAuth.Token);
        var userInfo = await userClient.User.Current();

        var account = await _ghContext.Accounts
          .Include(x => x.AuthenticationToken)
          .SingleOrDefaultAsync(x => x.Id == userInfo.Id);
        if (account == null) {
          account = _ghContext.Accounts.Create();
        }
        account.AvatarUrl = userInfo.AvatarUrl;
        account.Company = userInfo.Company ?? "";
        account.CreatedAt = userInfo.CreatedAt.UtcDateTime;
        account.Id = userInfo.Id;
        account.Login = userInfo.Login;
        account.Name = userInfo.Name;

        if (account.AuthenticationToken == null) {
          account.AuthenticationToken = _ghContext.AuthenticationTokens.Add(new GitHubAuthenticationTokenModel() {
            Account = account,
          });
        }
        account.AuthenticationToken.AccessToken = accessToken;
        account.AuthenticationToken.Scopes = appAuth.ScopesDelimited;

        await _ghContext.SaveChangesAsync();

        return Ok();
      } catch {
        // TODO: Scope this more tightly
        return Unauthorized();
      }
    }

    [HttpGet("begin")]
    public IActionResult BeginGitHubOauth() {
      var uri = new UriBuilder(Uri.UriSchemeHttps, Request.Host.Host, Request.Host.Port ?? 443, Url.Action($"{nameof(EndGitHubOauth)}"));

      var request = new OauthLoginRequest(_ghOpts.ApplicationId) {
        RedirectUri = uri.Uri,
        State = "TODO: this",
      };

      foreach (var scope in _requiredOauthScopes.Union(_optionalOauthScopes)) {
        request.Scopes.Add(scope);
      }

      var client = _ghOpts.CreateApplicationClient();
      var url = client.Oauth.GetGitHubLoginUrl(request).ToString();
      return Redirect(url);
    }

    [HttpGet("end")]
    public async Task<IActionResult> EndGitHubOauth(string code, string state) {
      var client = _ghOpts.CreateApplicationClient();
      var token = await client.Oauth.CreateAccessToken(
        new OauthTokenRequest(_ghOpts.ApplicationId, _ghOpts.ApplicationSecret, code));
      return Json(token);
    }
  }
}
