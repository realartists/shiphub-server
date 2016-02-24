namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Microsoft.AspNet.Mvc;
  using Octokit;

  [Route("api/[controller]")]
  public class AuthenticationController : Controller {
    private const string ApplicationId = "3852a73fd5c85002499c";
    private const string ApplicationSecret = "e768601a3674e37b2b68f3194e7c74690bb35005";

    private static GitHubClient CreateGitHubClient(string accessToken = null) {
      var client = new GitHubClient(new ProductHeaderValue("ShipHub", "0.1"));
      if (accessToken == null) {
        client.Credentials = new Credentials(ApplicationId, ApplicationSecret);
      } else {
        client.Credentials = new Credentials(accessToken);
      }
      return client;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] string accessToken) {
      if (string.IsNullOrWhiteSpace(accessToken)) {
        return HttpBadRequest($"{nameof(accessToken)} is required.");
      }

      var gh = CreateGitHubClient();
      try {
        var appAuth = await gh.Authorization.CheckApplicationAuthentication(gh.Credentials.Login, accessToken);

        if (appAuth == null) {
          return HttpUnauthorized();
        }

        return Json(appAuth);
      } catch (Exception e) {
        throw;
      }
    }

    [HttpGet("begin")]
    public async Task<IActionResult> BeginGitHubOauth() {
      var gh = CreateGitHubClient();

      var hostParts = Request.Host.Value.Split(':');
      var host = hostParts.First();
      var port = hostParts.Skip(1).SingleOrDefault() ?? "443";
      var uri = new UriBuilder(Request.Scheme, host, int.Parse(port), Url.Action($"{nameof(EndGitHubOauth)}"));

      var request = new OauthLoginRequest(ApplicationId) {
        RedirectUri = uri.Uri, 
        State = "TODO: this",
      };
      request.Scopes.Add("repo");
      //request.Scopes.Add("admin:repo_hook");
      request.Scopes.Add("user:email");

      var url = gh.Oauth.GetGitHubLoginUrl(request).ToString();
      return Redirect(url);
    }

    [HttpGet("end")]
    public async Task<IActionResult> EndGitHubOauth(string code, string state) {
      var gh = CreateGitHubClient();
      var token = await gh.Oauth.CreateAccessToken(new OauthTokenRequest(ApplicationId, ApplicationSecret, code));
      return Json(token);
    }
  }
}
