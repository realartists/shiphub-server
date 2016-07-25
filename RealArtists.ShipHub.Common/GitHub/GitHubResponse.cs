namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Net;

  public class GitHubResponse {
    public string AcceptHeaderOverride { get; set; }
    public Uri RequestUri { get; set; }
    public IGitHubCredentials Credentials { get; set; }
    public HttpStatusCode Status { get; set; }
    public DateTimeOffset Date { get; set; }
    public HashSet<string> Scopes { get; set; } = new HashSet<string>();

    public bool IsError { get; set; }
    public GitHubError Error { get; set; }

    public GitHubCacheData CacheData { get; set; }
    public GitHubRateLimit RateLimit { get; set; }
    public GitHubRedirect Redirect { get; set; }
    public GitHubPagination Pagination { get; set; }
  }

  public class GitHubResponse<T> : GitHubResponse {
    private bool _resultSet = false;
    private T _result = default(T);
    public T Result {
      get {
        if (IsError) {
          throw new InvalidOperationException("Cannot get the result of failed request.");
        }

        if (!_resultSet) {
          throw new InvalidOperationException("Cannot get the result before it's set.");
        }

        return _result;
      }
      set {
        // Allow results to be set multiple times because I'm lazy and pagination uses it.
        if (IsError) {
          throw new InvalidOperationException("Cannot set the result of failed request.");
        }

        _result = value;
        _resultSet = true;
      }
    }
  }
}
