namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;

  public class GitHubRequest {
    public GitHubRequest(string path, GitHubCacheDetails opts = null, RequestPriority priority = RequestPriority.Background)
      : this(HttpMethod.Get, path, opts, priority) { }

    public GitHubRequest(HttpMethod method, string path, RequestPriority priority = RequestPriority.Background)
      : this(method, path, null, priority) { }

    private GitHubRequest(HttpMethod method, string path, GitHubCacheDetails opts, RequestPriority priority) {
      if (path.IsNullOrWhiteSpace() || path.Contains('?')) {
        throw new ArgumentException($"path must be non null and cannot contain query parameters. provided: {path}", nameof(path));
      }

      Method = method;
      Path = path;
      Priority = priority;
      CacheOptions = opts;
    }

    public string AcceptHeaderOverride { get; set; }
    public GitHubCacheDetails CacheOptions { get; set; }
    public HttpMethod Method { get; set; }
    public string Path { get; set; }
    public RequestPriority Priority { get; set; }
    public DateTimeOffset CreationDate { get; } = DateTimeOffset.UtcNow;

    public Dictionary<string, string> Parameters { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private Uri _uri;
    public Uri Uri {
      get {
        if (_uri == null) {
          if (Parameters.Any()) {
            var parms = Parameters
              .OrderBy(x => x.Key)
              .Select(x => string.Join("=", WebUtility.UrlEncode(x.Key), WebUtility.UrlEncode(x.Value)));
            var query = string.Join("&", parms);
            _uri = new Uri(string.Join("?", Path, query), UriKind.Relative);
          } else {
            _uri = new Uri(Path, UriKind.Relative);
          }
        }

        return _uri;
      }
    }

    public GitHubRequest CloneWithNewUri(Uri uri) {
      if (!uri.IsAbsoluteUri) {
        throw new ArgumentException($"Only absolute URIs are supported. Given: {uri}", nameof(uri));
      }

      var clone = new GitHubRequest(Method, uri.GetComponents(UriComponents.Path, UriFormat.Unescaped), priority:Priority) {
        AcceptHeaderOverride = AcceptHeaderOverride,
      };

      var parsed = uri.ParseQueryString();
      for (var i = 0; i < parsed.Count; ++i) {
        clone.AddParameter(parsed.GetKey(i), parsed.GetValues(i).Single());
      }

      return clone;
    }

    public GitHubRequest AddParameter(string key, string value) {
      if (_uri != null) {
        throw new InvalidOperationException("Cannot change parameters after request Uri has been used.");
      }
      Parameters.Add(key, value);
      return this;
    }

    public GitHubRequest AddParameter(string key, object value) {
      return AddParameter(key, value.ToString());
    }

    public GitHubRequest AddParameter(string key, DateTime value) {
      return AddParameter(key, value.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'"));
    }

    public GitHubRequest AddParameter(string key, DateTimeOffset value) {
      return AddParameter(key, value.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'"));
    }

    public virtual HttpContent CreateBodyContent() {
      return null;
    }
  }

  /// <summary>
  /// Used to represent a GitHub API request with a body.
  /// 
  /// Special care has been taken to ensure all member variables are simple objects
  /// that can be serialized across Orleans process boundaries.
  /// </summary>
  /// <typeparam name="T">The type of the body content.</typeparam>
  public class GitHubRequest<T> : GitHubRequest
    where T : class {
    public GitHubRequest(HttpMethod method, string path, T body, RequestPriority priority)
      : base(method, path, priority) {
      Body = body;
    }

    public T Body { get; set; }

    public override HttpContent CreateBodyContent() {
      return new ObjectContent<T>(Body, GitHubSerialization.JsonMediaTypeFormatter, GitHubSerialization.JsonMediaType);
    }
  }
}
