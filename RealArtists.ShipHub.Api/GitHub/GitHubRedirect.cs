namespace RealArtists.ShipHub.Api.GitHub {
  using System;
  using System.Net;

  public class GitHubRedirect {
    public GitHubRedirect() { }

    public GitHubRedirect(HttpStatusCode status, Uri originalLocation, Uri newLocation, GitHubRedirect previous = null) {
      Status = status;
      OriginalLocation = originalLocation;
      NewLocation = newLocation;
      PreviousRedirect = previous;
    }

    public HttpStatusCode Status { get; set; }
    public Uri OriginalLocation { get; set; }
    public Uri NewLocation { get; set; }
    public GitHubRedirect PreviousRedirect { get; set; }
  }
}
