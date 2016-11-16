﻿namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;

  public class GitHubResponse {
    protected GitHubResponse() { }
    public GitHubResponse(GitHubRequest request) {
      _request = request;
    }

    [NonSerialized]
    private GitHubRequest _request;

    public GitHubRequest Request { get { return _request; } }
    public HttpStatusCode Status { get; set; }
    public DateTimeOffset Date { get; set; }
    public HashSet<string> Scopes { get; } = new HashSet<string>();

    public bool Succeeded { get { return (int)Status >= 200 && (int)Status < 400; } }

    public GitHubError Error { get; set; }
    public DateTimeOffset? RetryAfter { get; set; }

    public GitHubCacheDetails CacheData { get; set; }
    public GitHubRateLimit RateLimit { get; set; }
    public GitHubRedirect Redirect { get; set; }
    public GitHubPagination Pagination { get; set; }
  }

  public class GitHubResponse<T> : GitHubResponse {
    public GitHubResponse(GitHubRequest request) : base(request) {
    }

    private bool _resultSet = false;
    private T _result = default(T);
    public T Result {
      get {
        if (!_resultSet) {
          throw new InvalidOperationException("Cannot get the result before it's set.");
        }

        return _result;
      }
      set {
        // Allow results to be set multiple times because I'm lazy and pagination uses it.
        _result = value;
        _resultSet = true;
      }
    }
  }

  public static class GitHubResponseExtensions {
    public static GitHubResponse<IEnumerable<TResult>> Distinct<TResult, TKey>(this GitHubResponse<IEnumerable<TResult>> source, Func<TResult, TKey> keySelector) {
      if (source == null || source.Status != HttpStatusCode.OK) {
        return source;
      }

      source.Result = source.Result.Distinct(keySelector).ToArray();
      return source;
    }
  }
}
