namespace RealArtists.GitHub {
  using System;
  using System.Net;

  public class GitHubRedirect {
    public GitHubRedirect() { }

    public GitHubRedirect(HttpStatusCode status, Uri location) {
      Status = status;
      Location = location;
    }

    public HttpStatusCode Status { get; set; }
    public Uri Location { get; set; }
  }
}
