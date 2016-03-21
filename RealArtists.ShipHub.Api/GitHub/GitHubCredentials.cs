namespace RealArtists.ShipHub.Api.GitHub {
  using System;
  using System.Net.Http.Headers;
  using System.Text;

  public interface IGitHubCredentials {
    void Apply(HttpRequestHeaders headers);
  }

  public class GitHubApplicationCredentials : IGitHubCredentials {
    private AuthenticationHeaderValue _authHeader;

    public GitHubApplicationCredentials(string applicationId, string applicationSecret) {
      var creds = $"{applicationId}:{applicationSecret}";
      var credBytes = Encoding.ASCII.GetBytes(creds);
      var cred64 = Convert.ToBase64String(credBytes);
      _authHeader = new AuthenticationHeaderValue("basic", cred64);
    }

    public void Apply(HttpRequestHeaders headers) {
      headers.Authorization = _authHeader;
    }
  }

  public class GitHubOauthCredentials : IGitHubCredentials {
    private AuthenticationHeaderValue _authHeader;

    public GitHubOauthCredentials(string accessToken) {
      _authHeader = new AuthenticationHeaderValue("token", accessToken);
    }

    public void Apply(HttpRequestHeaders headers) {
      headers.Authorization = _authHeader;
    }
  }
}
