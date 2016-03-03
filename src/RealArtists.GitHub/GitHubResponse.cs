namespace RealArtists.GitHub {
  using System;
  using System.Net;

  public class GitHubResponse<T> {
    public HttpStatusCode Status { get; set; }
    public GitHubError Error { get; set; }
    public GitHubRedirect Redirect { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string ETag { get; set; }
    public T Result { get; set; }
  }
}
