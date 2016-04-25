namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;

  public class GitHubRequest {
    public GitHubRequest(HttpMethod method, string path, IGitHubCacheOptions opts = null) {
      Method = method;
      Path = path;
      ETag = opts?.ETag;
      LastModified = opts?.LastModified;
    }

    public HttpMethod Method { get; set; }
    public string Path { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }

    // Arguably should allow duplicate keys, but no. Don't do that.
    // Should it be case sensitive? Meh.
    public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

    public Uri Uri {
      get {
        if (Parameters.Any()) {
          var parms = Parameters.Select(x => string.Join("=", WebUtility.UrlEncode(x.Key), WebUtility.UrlEncode(x.Value)));
          var query = string.Join("&", parms);
          return new Uri(string.Join("?", Path, query), UriKind.Relative);
        } else {
          return new Uri(Path, UriKind.Relative);
        }
      }
      set {
        if (!value.IsAbsoluteUri) {
          throw new ArgumentException($"Uri is not absolute: {value}", nameof(value));
        }

        Path = value.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        ETag = null;
        LastModified = null;

        Parameters.Clear();
        var parsed = value.ParseQueryString();
        for (int i = 0; i < parsed.Count; ++i) {
          Parameters.Add(parsed.GetKey(i), parsed.GetValues(i).Single());
        }
      }
    }

    public GitHubRequest AddParameter(string key, string value) {
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
    public GitHubRequest(HttpMethod method, string path, T body, IGitHubCacheOptions opts = null)
      : base(method, path, opts) {
      Body = body;
    }

    public T Body { get; set; }

    public override HttpContent CreateBodyContent() {
      return new ObjectContent<T>(Body, GitHubClient.JsonMediaTypeFormatter, GitHubClient.JsonMediaType);
    }
  }
}
