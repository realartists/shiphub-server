namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Threading;
  using System.Threading.Tasks;
  using Models;

  public class GitHubClient : IGitHubClient {
    public Uri ApiRoot { get; } = new Uri("https://api.github.com/");
    public string DefaultToken { get; set; }
    public ProductInfoHeaderValue UserAgent { get; }
    public string UserInfo { get; }
    public Guid CorrelationId { get; }

    // Rate limit concurrency requires some finesse
    private object _rateLimitLock = new object();
    private GitHubRateLimit _rateLimit;
    public GitHubRateLimit RateLimit { get { return _rateLimit; } }

    public GitHubClient(IGitHubHandler handler, string productName, string productVersion, string userInfo, Guid correlationId, string accessToken = null, GitHubRateLimit rateLimit = null) {
      Handler = handler;
      DefaultToken = accessToken;
      UserAgent = new ProductInfoHeaderValue(productName, productVersion);
      UserInfo = userInfo;
      CorrelationId = correlationId;
      _rateLimit = rateLimit;
    }

    public void UpdateInternalRateLimit(GitHubRateLimit rateLimit) {
      lock (_rateLimitLock) {
        if (_rateLimit == null
          || _rateLimit.RateLimitReset < rateLimit.RateLimitReset
          || _rateLimit.RateLimitRemaining > rateLimit.RateLimitRemaining) {
          Interlocked.Exchange(ref _rateLimit, rateLimit);
        }
      }
    }

    public IGitHubHandler Handler { get; set; }

    private Task<GitHubResponse<T>> Fetch<T>(GitHubRequest request) {
      return Handler.Fetch<T>(this, request);
    }

    private Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubRequest request, Func<T, TKey> keySelector) {
      return Handler.FetchPaged(this, request, keySelector);
    }

    /// ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public Task<GitHubResponse<Account>> User(IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest("user", cacheOptions, restricted: true);
      return Fetch<Account>(request);
    }

    public Task<GitHubResponse<IEnumerable<Repository>>> Repositories(IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest("user/repos", cacheOptions, restricted: true);
      return FetchPaged(request, (Repository x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/timeline", cacheOptions, restricted: true) {
        // Timeline support (application/vnd.github.mockingbird-preview+json)
        // https://developer.github.com/changes/2016-05-23-timeline-preview-api/
        AcceptHeaderOverride = "application/vnd.github.mockingbird-preview+json",
      };
      // Ugh. Uniqueness here is hard because IDs aren't.
      return FetchPaged(request, (IssueEvent x) => Tuple.Create(x.Event, x.Id, x.ExtensionDataDictionary.Val("url")));
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{number}", cacheOptions);
      return Fetch<Issue>(request);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = null, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues", cacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("state", "all");
      request.AddParameter("sort", "updated");

      return FetchPaged(request, (Issue x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/reactions", cacheOptions) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments/{commentId}/reactions", cacheOptions) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = null, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/issues/{issueNumber}/comments", cacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      return FetchPaged(request, (Comment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = null, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/issues/comments", cacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (Comment x) => x.Id);
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/commits/{hash}", cacheOptions, restricted:true);
      return Fetch<Commit>(request);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/issues/events", cacheOptions);
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (IssueEvent x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/labels", cacheOptions);
      return FetchPaged(request, (Label x) => Tuple.Create(x.Name, x.Color));
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/milestones", cacheOptions);
      request.AddParameter("state", "all");
      return FetchPaged(request, (Milestone x) => x.Id);
    }

    public async Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest("user/memberships/orgs", cacheOptions, restricted:true);
      var result = await FetchPaged(request, (OrganizationMembership x) => x.Organization.Id);

      if (!result.IsError) {
        // Seriously GitHub?
        foreach (var membership in result.Result) {
          membership.Organization.Type = GitHubAccountType.Organization;
        }
      }

      return result;
    }

    public Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", IGitHubCacheDetails cacheOptions = null) {
      // defaults: filter=all, role=all
      var request = new GitHubRequest($"/orgs/{orgLogin}/members", cacheOptions);
      request.AddParameter("role", role);
      return FetchPaged(request, (Account x) => x.Id);
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/pulls/{pullRequestNumber}", cacheOptions);
      return Fetch<PullRequest>(request);
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/assignees", cacheOptions);
      return FetchPaged(request, (Account x) => x.Id);
    }

    public async Task<GitHubResponse<bool>> IsAssignable(string repoFullName, string login) {
      // Pass empty cache options to ensure we get a response. We need the result.
      var request = new GitHubRequest($"/repos/{repoFullName}/assignees/{login}", GitHubCacheDetails.Empty);
      var response = await Fetch<bool>(request);
      response.IsError = false;
      switch (response.Status) {
        case HttpStatusCode.NotFound:
          response.Result = false;
          break;
        case HttpStatusCode.NoContent:
          response.Result = true;
          break;
        default:
          response.IsError = true;
          break;
      }
      return response;
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"/orgs/{name}/hooks", cacheOptions);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, IGitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/hooks", cacheOptions);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"/repos/{repoFullName}/hooks", hook);
      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"/orgs/{orgName}/hooks", hook);
      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId) {
      var request = new GitHubRequest(HttpMethod.Delete, $"/repos/{repoFullName}/hooks/{hookId}");
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId) {
      var request = new GitHubRequest(HttpMethod.Delete, $"/orgs/{orgName}/hooks/{hookId}");
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, string[] events) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"/repos/{repoFullName}/hooks/{hookId}",
        new {
          Events = events,
        });

      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, string[] events) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"/orgs/{orgName}/hooks/{hookId}",
        new {
          Events = events,
        });

      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId) {
      var request = new GitHubRequest(HttpMethod.Post, $"/orgs/{name}/hooks/{hookId}/pings");
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId) {
      var request = new GitHubRequest(HttpMethod.Post,$"/repos/{repoFullName}/hooks/{hookId}/pings");
      return Fetch<bool>(request);
    }

    private int _requestId = 0;
    public int NextRequestId() {
      return Interlocked.Increment(ref _requestId);
    }
  }
}
