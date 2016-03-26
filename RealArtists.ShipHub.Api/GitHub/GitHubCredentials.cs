namespace RealArtists.ShipHub.Api.GitHub {
  using System;
  using System.Net.Http.Headers;
  using System.Text;

  public interface IGitHubCredentials {
    string Scheme { get; }
    string Parameter { get; }
    void Apply(HttpRequestHeaders headers);
  }

  public class GitHubCredentials : IGitHubCredentials {
    private AuthenticationHeaderValue _authHeader;

    public GitHubCredentials(AuthenticationHeaderValue authHeader) {
      _authHeader = authHeader;
    }

    public string Scheme { get { return _authHeader.Scheme; } }

    public string Parameter { get { return _authHeader.Parameter; } }

    public void Apply(HttpRequestHeaders headers) {
      headers.Authorization = _authHeader;
    }

    public static GitHubCredentials ForToken(string accessToken) {
      return new GitHubCredentials(new AuthenticationHeaderValue("token", accessToken));
    }

    public static GitHubCredentials ForApplication(string applicationId, string applicationSecret) {
      var creds = $"{applicationId}:{applicationSecret}";
      var credBytes = Encoding.ASCII.GetBytes(creds);
      var cred64 = Convert.ToBase64String(credBytes);
      return new GitHubCredentials(new AuthenticationHeaderValue("basic", cred64));
    }
  }
}
