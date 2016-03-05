namespace RealArtists.GitHub {
  using System;
  using System.Net;

  public class GitHubResponse {
    public HttpStatusCode Status { get; set; }

    // Lazy properties. Null unless needed.
    public GitHubError Error { get; set; }
    public GitHubRedirect Redirect { get; set; }
    public GitHubPagination Pagination { get; set; }

    // Cache management
    public string ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }

    // Rate limit tracking
    public int RateLimit { get; set; }
    public int RateLimitRemaining { get; set; }
    public DateTimeOffset RateLimitReset { get; set; }
  }

  public class GitHubResponse<T> : GitHubResponse {
    public T Result { get; set; }
  }
}
