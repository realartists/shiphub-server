namespace RealArtists.ShipHub.Api.GitHub {
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Formatting;
  using System.Net.Http.Headers;
  using System.Threading.Tasks;
  using Models;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Converters;
  using Newtonsoft.Json.Linq;
  using Serialization;

  public static class HeaderUtility {
    public static T ParseHeader<T>(this HttpResponseMessage response, string headerName, Func<string, T> selector) {
      var header = response.Headers
        .Where(x => x.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase))
        .SelectMany(x => x.Value)
        .SingleOrDefault();

      return selector(header);
    }
  }

  public class GitHubClient : IDisposable {
#if DEBUG
    public const bool UseFiddler = true;
#else
    public const bool UseFiddler = false;
#endif

    static readonly Uri _ApiRoot = new Uri("https://api.github.com/");
    static readonly Uri _OauthTokenRedemption = new Uri("https://github.com/login/oauth/access_token");
    static readonly MediaTypeFormatter[] _MediaTypeFormatters;

    public static readonly JsonSerializerSettings JsonSettings;
    public static readonly MediaTypeHeaderValue JsonMediaType = new MediaTypeHeaderValue("application/json");
    public static readonly JsonMediaTypeFormatter JsonMediaTypeFormatter;

    static GitHubClient() {
      JsonSettings = new JsonSerializerSettings() {
        ContractResolver = new SnakeCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Include,
      };
      JsonSettings.Converters.Add(new StringEnumConverter() {
        AllowIntegerValues = false,
        CamelCaseText = true,
      });
      JsonMediaTypeFormatter = new JsonMediaTypeFormatter() {
        SerializerSettings = JsonSettings,
      };
      _MediaTypeFormatters = new[] { JsonMediaTypeFormatter };

      if (UseFiddler) {
        ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => { return true; };
      }
    }

    private HttpClient _httpClient;

    public IGitHubCredentials DefaultCredentials { get; set; }

    public GitHubClient(string productName, string productVersion, IGitHubCredentials credentials = null) {
      DefaultCredentials = credentials;

      // TODO: Only one handler for all clients?
      var handler = new HttpClientHandler() {
        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
        AllowAutoRedirect = false,
        MaxRequestContentBufferSize = 4 * 1024 * 1024,
        UseCookies = false,
        UseDefaultCredentials = false,
        UseProxy = UseFiddler,
        Proxy = UseFiddler ? new WebProxy("127.0.0.1", 8888) : null,
      };
      _httpClient = new HttpClient(handler, true);

      var headers = _httpClient.DefaultRequestHeaders;
      headers.AcceptEncoding.Clear();
      headers.AcceptEncoding.ParseAdd("gzip");
      headers.AcceptEncoding.ParseAdd("deflate");

      headers.Accept.Clear();
      headers.Accept.ParseAdd("application/vnd.github.v3+json");

      headers.AcceptCharset.Clear();
      headers.AcceptCharset.ParseAdd("utf-8");

      headers.UserAgent.Clear();
      headers.UserAgent.Add(new ProductInfoHeaderValue(productName, productVersion));

      headers.Add("Time-Zone", "Etc/UTC");
    }

    public async Task<GitHubResponse<CreatedAccessToken>> CreateAccessToken(string clientId, string clientSecret, string code, string state) {
      var request = new GitHubRequest<object>(HttpMethod.Post, "", new {
        ClientId = clientId,
        ClientSecret = clientSecret,
        Code = code,
        State = state,
      });

      var httpRequest = new HttpRequestMessage(request.Method, _OauthTokenRedemption) {
        Content = request.CreateBodyContent(),
      };
      httpRequest.Headers.Accept.Clear();
      httpRequest.Headers.Accept.ParseAdd("application/json");

      var response = await _httpClient.SendAsync(httpRequest);

      var result = new GitHubResponse<CreatedAccessToken>() {
        Status = response.StatusCode,
      };

      if (response.IsSuccessStatusCode) {
        var temp = await response.Content.ReadAsAsync<JToken>(_MediaTypeFormatters);
        if (temp["error"] != null) {
          result.Error = JsonRoundTrip<GitHubError>(temp);
        } else {
          result.Result = JsonRoundTrip<CreatedAccessToken>(temp);
        }
      } else {
        result.Error = await response.Content.ReadAsAsync<GitHubError>(_MediaTypeFormatters);
      }

      return result;
    }

    public Task<GitHubResponse<Models.Authorization>> CheckAccessToken(string clientId, string accessToken) {
      var request = new GitHubRequest(HttpMethod.Get, $"/applications/{clientId}/tokens/{accessToken}");
      return MakeRequest<Models.Authorization>(request);
    }

    public Task<GitHubResponse<Account>> User(IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, "user", opts?.CacheOptions);
      return MakeRequest<Account>(request, opts);
    }

    public Task<GitHubResponse<Account>> User(string login, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"users/{login}", opts?.CacheOptions);
      return MakeRequest<Account>(request, opts);
    }

    public async Task<GitHubResponse<IEnumerable<Repository>>> Repositories(IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, "user/repos", opts?.CacheOptions);
      var result = await MakeRequest<IEnumerable<Repository>>(request, opts);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await Enumerate(result);
      }
    }

    public Task<GitHubResponse<Repository>> Repository(string repoFullName, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}", opts?.CacheOptions);
      return MakeRequest<Repository>(request, opts);
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int issueNumber, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/issues/{issueNumber}", opts?.CacheOptions);
      return MakeRequest<Issue>(request, opts);
    }

    public async Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = null, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/issues", opts?.CacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("state", "all");
      request.AddParameter("sort", "updated");
      var result = await MakeRequest<IEnumerable<Issue>>(request, opts);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await Enumerate(result);
      }
    }

    public Task<GitHubResponse<Comment>> Comment(string repoFullName, int commentId, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/issues/comments/{commentId}", opts?.CacheOptions);
      return MakeRequest<Comment>(request, opts);
    }

    public async Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueId, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"/repos/{repoFullName}/issues/comments", opts?.CacheOptions);
      var result = await MakeRequest<IEnumerable<Comment>>(request, opts);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await Enumerate(result);
      }
    }

    public async Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/labels", opts?.CacheOptions);
      var result = await MakeRequest<IEnumerable<Label>>(request, opts);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await Enumerate(result);
      }
    }

    public Task<GitHubResponse<Comment>> Milestone(string repoFullName, int milestoneNumber, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/milestones/{milestoneNumber}", opts?.CacheOptions);
      return MakeRequest<Comment>(request, opts);
    }

    public async Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/milestones", opts?.CacheOptions);
      request.AddParameter("state", "all");

      var result = await MakeRequest<IEnumerable<Milestone>>(request, opts);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await Enumerate(result);
      }
    }

    public Task<GitHubResponse<Account>> Organization(string login, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"orgs/{login}", opts?.CacheOptions);
      return MakeRequest<Account>(request, opts);
    }

    public async Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, IGitHubRequestOptions opts = null) {
      var request = new GitHubRequest(HttpMethod.Get, $"repos/{repoFullName}/assignees", opts?.CacheOptions);
      var result = await MakeRequest<IEnumerable<Account>>(request, opts);
      if (result.IsError || result.Pagination == null) {
        return result;
      } else {
        return await Enumerate(result);
      }
    }

    public async Task<GitHubResponse<T>> MakeRequest<T>(GitHubRequest request, IGitHubRequestOptions opts = null, GitHubRedirect redirect = null) {
      // Always request the biggest page size
      if (request.Method == HttpMethod.Get
        && typeof(IEnumerable).IsAssignableFrom(typeof(T))) {
        request.AddParameter("per_page", 100);
      }

      var uri = new Uri(_ApiRoot, request.Uri);
      var httpRequest = new HttpRequestMessage(request.Method, uri) {
        Content = request.CreateBodyContent(),
      };

      // Conditional headers
      httpRequest.Headers.IfModifiedSince = request.LastModified;
      if (!string.IsNullOrWhiteSpace(request.ETag)) {
        httpRequest.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(request.ETag));
      }

      // Authentication
      var creds = opts?.Credentials ?? DefaultCredentials;
      creds?.Apply(httpRequest.Headers);

      var response = await _httpClient.SendAsync(httpRequest);

      // Handle redirects
      switch (response.StatusCode) {
        case HttpStatusCode.MovedPermanently:
        case HttpStatusCode.RedirectKeepVerb:
          request.Uri = response.Headers.Location;
          return await MakeRequest<T>(request, opts, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        case HttpStatusCode.Redirect:
        case HttpStatusCode.RedirectMethod:
          request.Method = HttpMethod.Get;
          request.Uri = response.Headers.Location;
          return await MakeRequest<T>(request, opts, new GitHubRedirect(response.StatusCode, uri, request.Uri, redirect));
        default:
          break;
      }

      var result = new GitHubResponse<T>() {
        Credentials = creds,
        Redirect = redirect,
        RequestUri = response.RequestMessage.RequestUri,
        Status = response.StatusCode,
        IsError = !response.IsSuccessStatusCode,
      };

      // Cache Headers
      result.ETag = response.Headers.ETag?.Tag;
      result.LastModified = response.Content?.Headers?.LastModified;

      // Expires and Caching Max-Age
      result.Expires = response.Content?.Headers?.Expires;
      var maxAgeSpan = response.Headers.CacheControl?.SharedMaxAge ?? response.Headers.CacheControl?.MaxAge;
      if (maxAgeSpan != null) {
        var maxAgeExpires = DateTimeOffset.UtcNow.Add(maxAgeSpan.Value);
        if (result.Expires == null || maxAgeExpires < result.Expires) {
          result.Expires = maxAgeExpires;
        }
      }

      // Rate Limits
      result.RateLimit = response.ParseHeader("X-RateLimit-Limit", x => int.Parse(x));
      result.RateLimitRemaining = response.ParseHeader("X-RateLimit-Remaining", x => int.Parse(x));
      result.RateLimitReset = response.ParseHeader("X-RateLimit-Reset", x => EpochUtility.ToDateTimeOffset(int.Parse(x)));

      // Pagination
      // Screw the RFC, minimally match what GitHub actually sends.
      result.Pagination = response.ParseHeader("Link", x => (x == null) ? null : GitHubPagination.FromLinkHeader(x));

      if (response.IsSuccessStatusCode) {
        // TODO: Handle accepted, no content, etc.
        if (response.StatusCode != HttpStatusCode.NotModified) {
          result.Result = await response.Content.ReadAsAsync<T>(_MediaTypeFormatters);
        }
      } else {
        result.Error = await response.Content.ReadAsAsync<GitHubError>(_MediaTypeFormatters);
      }

      return result;
    }

    public async Task<GitHubResponse<IEnumerable<T>>> Enumerate<T>(GitHubResponse<IEnumerable<T>> firstPage, IGitHubRequestOptions opts = null) {
      var results = new List<T>(firstPage.Result);
      var current = firstPage;

      // TODO: Request in parallel?
      while (current.Pagination?.Next != null) {
        // Ignore cache options, since we're paging because they didn't match
        var nextReq = new GitHubRequest(HttpMethod.Get, "") {
          Uri = current.Pagination.Next,
        };
        current = await MakeRequest<IEnumerable<T>>(nextReq, opts);

        if (current.IsError) {
          return current;
        } else {
          results.AddRange(current.Result);
        }
      }

      // Keep cache and other headers from first page.
      var final = firstPage;
      final.Result = results;
      final.RateLimit = current.RateLimit;
      final.RateLimitRemaining = current.RateLimitRemaining;
      final.RateLimitReset = current.RateLimitReset;
      return final;
    }

    public static string SerializeObject(object value) {
      return JsonConvert.SerializeObject(value, JsonSettings);
    }

    public static T DeserializeObject<T>(string json) {
      return JsonConvert.DeserializeObject<T>(json, JsonSettings);
    }

    public static T JsonRoundTrip<T>(object self) {
      return DeserializeObject<T>(SerializeObject(self));
    }

    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing) {
      if (!disposedValue) {
        if (disposing) {
          _httpClient.Dispose();
          _httpClient = null;
        }

        disposedValue = true;
      }
    }
    public void Dispose() {
      Dispose(true);
    }
  }
}

