namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Net;

  public class GitHubResponse {
    public Uri RequestUri { get; set; }
    public HttpStatusCode Status { get; set; }
    public bool IsError { get; set; }
    public IGitHubCredentials Credentials { get; set; }

    // Null unless sent.
    public GitHubError Error { get; set; }
    public GitHubRedirect Redirect { get; set; }
    public GitHubPagination Pagination { get; set; }

    // Cache management
    public string ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? Expires { get; set; }

    // Rate limit tracking
    public int RateLimit { get; set; }
    public int RateLimitRemaining { get; set; }
    public DateTimeOffset RateLimitReset { get; set; }
  }

  public class GitHubResponse<T> : GitHubResponse {
    private bool _resultSet = false;
    private T _result = default(T);
    public T Result {
      get {
        if (IsError) {
          throw new InvalidOperationException("Cannot access result of failed request.");
        }

        if (!_resultSet) {
          throw new InvalidOperationException("Cannot access result before it's set.");
        }

        return _result;
      }
      set {
        if (IsError) {
          throw new InvalidOperationException("Cannot access result of failed request.");
        }

        // Allow results to be set multiple times because I'm lazy and pagination uses it.

        _result = value;
        _resultSet = true;
      }
    }
  }
}
