namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Configuration;
  using Microsoft.AspNet.Mvc;
  using Microsoft.Extensions.OptionsModel;
  using Octokit;

  [Route("api/[controller]")]
  public class AuthenticationController : Controller {
    private IGitHubClient _ghClient;
    private GitHubOptions _ghOpts;

    public AuthenticationController(IGitHubClient ghClient, IOptions<GitHubOptions> ghOpts) {
      _ghClient = ghClient;
      _ghOpts = ghOpts.Value;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] string accessToken) {
      if (string.IsNullOrWhiteSpace(accessToken)) {
        return HttpBadRequest($"{nameof(accessToken)} is required.");
      }

      try {
        var appAuth = await _ghClient.Authorization.CheckApplicationAuthentication(_ghOpts.ApplicationId, accessToken);

        if (appAuth == null) {
          return HttpUnauthorized();
        }

        return Json(appAuth);
      } catch {
        // TODO: Scope this more tightly
        return HttpUnauthorized();
      }
    }

    [HttpGet("begin")]
    public IActionResult BeginGitHubOauth() {
      var hostParts = Request.Host.Value.Split(':');
      var host = hostParts.First();
      var port = hostParts.Skip(1).SingleOrDefault() ?? "443";
      var uri = new UriBuilder(Request.Scheme, host, int.Parse(port), Url.Action($"{nameof(EndGitHubOauth)}"));

      var request = new OauthLoginRequest(_ghOpts.ApplicationId) {
        RedirectUri = uri.Uri,
        State = "TODO: this",
      };
      request.Scopes.Add("repo");
      request.Scopes.Add("user:email");

      var url = _ghClient.Oauth.GetGitHubLoginUrl(request).ToString();
      return Redirect(url);
    }

    [HttpGet("end")]
    public async Task<IActionResult> EndGitHubOauth(string code, string state) {
      var token = await _ghClient.Oauth.CreateAccessToken(
        new OauthTokenRequest(_ghOpts.ApplicationId, _ghOpts.ApplicationSecret, code));
      return Json(token);
    }
  }
}
