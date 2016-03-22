namespace RealArtists.ShipHub.Api.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Collections.Specialized;
  using System.Net;
  using System.Net.Http;

  public class GitHubRequest {
    public GitHubRequest(HttpMethod method, string path, IGitHubCacheOptions opts = null) {
      Method = method;
      Path = path;
      ETag = opts?.ETag;
      LastModified = opts?.LastModified;
      Parameters = new NameValueCollection();
    }

    public HttpMethod Method { get; set; }
    public string Path { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public NameValueCollection Parameters { get; set; }

    public Uri Uri {
      get {
        if (Parameters.Count > 0) {
          var parms = new List<string>(Parameters.Count);
          foreach (string key in Parameters.AllKeys) {
            foreach (var val in Parameters.GetValues(key)) {
              parms.Add(string.Join("=", WebUtility.UrlEncode(key), WebUtility.UrlEncode(val)));
            }
          }
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
        Parameters = value.ParseQueryString();
        ETag = null;
        LastModified = null;
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
