namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;

  public class GitHubRequest {
    public GitHubRequest(HttpMethod method, string path)
      : this(method, path, null, true) { }

    public GitHubRequest(string path, IGitHubCacheDetails opts = null, bool restricted = false)
      : this(HttpMethod.Get, path, opts, restricted) { }

    private GitHubRequest(HttpMethod method, string path, IGitHubCacheDetails opts, bool restricted) {
      if (path.IsNullOrWhiteSpace() || path.Contains('?')) {
        throw new ArgumentException($"path must be non null and cannot contain query parameters. provided: {path}", nameof(path));
      }

      Method = method;
      Path = path;
      _cacheOptions = opts;
      Restricted = restricted;
    }

    public string AcceptHeaderOverride { get; set; }
    public HttpMethod Method { get; set; }
    public string Path { get; set; }

    /// <summary>
    /// Set to true to restrict pipeline handlers from changing the access token used for the request.
    /// </summary>
    public bool Restricted { get; }

    private IGitHubCacheDetails _cacheOptions;
    public IGitHubCacheDetails CacheOptions {
      get { return _cacheOptions; }
      set {
        if (Restricted && value != null) {
          throw new InvalidOperationException("Cannot change cache options on restricted requests.");
        }
        _cacheOptions = value;
      }
    }

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

    public GitHubRequest CloneWithNewUri(Uri uri, bool preserveCache = false) {
      if (!uri.IsAbsoluteUri) {
        throw new ArgumentException($"Only absolute URIs are supported. Given: {uri}", nameof(uri));
      }

      // TODO: Retain cache options? I think it's important to preserve credentials at least.
      IGitHubCacheDetails cache = null;
      if (preserveCache || CacheOptions == GitHubCacheDetails.Empty) {
        cache = _cacheOptions;
      }

      var clone = new GitHubRequest(uri.GetComponents(UriComponents.Path, UriFormat.Unescaped), cache, Restricted) {
        Method = Method,
        AcceptHeaderOverride = AcceptHeaderOverride,
      };

      var parsed = uri.ParseQueryString();
      for (int i = 0; i < parsed.Count; ++i) {
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
      return AddParameter(key, value.ToUniversalTime().ToString("o"));
    }

    public GitHubRequest AddParameter(string key, DateTimeOffset value) {
      return AddParameter(key, value.ToString("o"));
    }

    public virtual HttpContent CreateBodyContent() {
      return null;
    }
  }

  public class GitHubRequest<T> : GitHubRequest
    where T : class {
    public GitHubRequest(HttpMethod method, string path, T body)
      : base(method, path) {
      Body = body;
    }

    public T Body { get; set; }

    public override HttpContent CreateBodyContent() {
      return new ObjectContent<T>(Body, GitHubSerialization.JsonMediaTypeFormatter, GitHubSerialization.JsonMediaType);
    }
  }
}
