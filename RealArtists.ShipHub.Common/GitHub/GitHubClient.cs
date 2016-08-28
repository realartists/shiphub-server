﻿namespace RealArtists.ShipHub.Common.GitHub {
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
    public ProductInfoHeaderValue UserAgent { get; private set; }

    // Rate limit concurrency requires some finesse
    private object _rateLimitLock = new object();
    private GitHubRateLimit _rateLimit;
    public GitHubRateLimit RateLimit { get { return _rateLimit; } }

    public GitHubClient(string productName, string productVersion, string accessToken = null, GitHubRateLimit rateLimit = null) {
      UserAgent = new ProductInfoHeaderValue(productName, productVersion);
      DefaultToken = accessToken;
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

    public IGitHubHandler Handler { get; set; } = new GitHubHandler();

    private Task<GitHubResponse<T>> Fetch<T>(GitHubRequest request) {
      return Handler.Fetch<T>(this, request);
    }

    private Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubRequest request, Func<T, TKey> keySelector) {
      return Handler.FetchPaged(this, request, keySelector);
    }

    /// ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public Task<GitHubResponse<Account>> User(IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest("user", cacheOptions, restricted: true);
      return Fetch<Account>(request);
    }

    public Task<GitHubResponse<IEnumerable<Repository>>> Repositories(IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest("user/repos", cacheOptions, restricted: true);
      return FetchPaged(request, (Repository x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/timeline", cacheOptions, restricted: true) {
        // Timeline support (application/vnd.github.mockingbird-preview+json)
        // https://developer.github.com/changes/2016-05-23-timeline-preview-api/
        AcceptHeaderOverride = "application/vnd.github.mockingbird-preview+json",
      };
      // Ugh. Uniqueness here is hard because IDs aren't.
      return FetchPaged(request, (IssueEvent x) => Tuple.Create(x.Event, x.Id, x.ExtensionDataDictionary.Val("url")));
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{number}", cacheOptions);
      return Fetch<Issue>(request);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset? since = null, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues", cacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("state", "all");
      request.AddParameter("sort", "updated");

      return FetchPaged(request, (Issue x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/reactions", cacheOptions) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments/{commentId}/reactions", cacheOptions) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, int issueNumber, DateTimeOffset? since = null, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/issues/{issueNumber}/comments", cacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      return FetchPaged(request, (Comment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Comment>>> Comments(string repoFullName, DateTimeOffset? since = null, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/issues/comments", cacheOptions);
      if (since != null) {
        request.AddParameter("since", since);
      }
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (Comment x) => x.Id);
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/commits/{hash}", cacheOptions);
      return Fetch<Commit>(request);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/issues/events", cacheOptions);
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (IssueEvent x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/labels", cacheOptions);
      return FetchPaged(request, (Label x) => Tuple.Create(x.Name, x.Color));
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/milestones", cacheOptions);
      request.AddParameter("state", "all");
      return FetchPaged(request, (Milestone x) => x.Id);
    }

    public async Task<GitHubResponse<IEnumerable<Account>>> Organizations(IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest("user/orgs", cacheOptions);
      var result = await FetchPaged(request, (Account x) => x.Id);

      if (!result.IsError) {
        // Seriously GitHub?
        foreach (var org in result.Result) {
          org.Type = GitHubAccountType.Organization;
        }
      }

      return result;
    }

    public async Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest("user/memberships/orgs", cacheOptions);
      var result = await FetchPaged(request, (OrganizationMembership x) => x.Organization.Id);

      if (!result.IsError) {
        // Seriously GitHub?
        foreach (var membership in result.Result) {
          membership.Organization.Type = GitHubAccountType.Organization;
        }
      }

      return result;
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/pulls/{pullRequestNumber}", cacheOptions);
      return Fetch<PullRequest>(request);
    }

    public async Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role = "all", IGitHubCacheMetadata cacheOptions = null) {
      // defaults: filter=all, role=all
      var request = new GitHubRequest($"/orgs/{orgLogin}/members?role={role}", cacheOptions);
      var result = await Fetch<IEnumerable<Account>>(request);
      return result.Distinct(x => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"repos/{repoFullName}/assignees", cacheOptions);
      return FetchPaged(request, (Account x) => x.Id);
    }

    public async Task<GitHubResponse<bool>> IsAssignable(string repoFullName, string login, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/assignees/{login}", cacheOptions);
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

    public Task<GitHubResponse<IEnumerable<Webhook>>> OrgWebhooks(string name, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"/orgs/{name}/hooks", cacheOptions);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> RepoWebhooks(string repoFullName, IGitHubCacheMetadata cacheOptions = null) {
      var request = new GitHubRequest($"/repos/{repoFullName}/hooks", cacheOptions);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<Webhook>> AddRepoWebhook(string repoFullName, Webhook hook) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"/repos/{repoFullName}/hooks", hook);
      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> AddOrgWebhook(string orgName, Webhook hook) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"/orgs/{orgName}/hooks", hook);
      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> DeleteRepoWebhook(string repoFullName, long hookId) {
      var request = new GitHubRequest(HttpMethod.Delete, $"/repos/{repoFullName}/hooks/{hookId}");
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<bool>> DeleteOrgWebhook(string orgName, long hookId) {
      var request = new GitHubRequest(HttpMethod.Delete, $"/orgs/{orgName}/hooks/{hookId}");
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<Webhook>> EditRepoWebhookEvents(string repoFullName, long hookId, string[] events) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"/repos/{repoFullName}/hooks/{hookId}",
        new {
          Events = events,
        });

      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> EditOrgWebhookEvents(string orgName, long hookId, string[] events) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"/orgs/{orgName}/hooks/{hookId}",
        new {
          Events = events,
        });

      return Fetch<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> PingOrgWebhook(string name, long hookId) {
      var request = new GitHubRequest<object>(
        new HttpMethod("POST"),
        $"/orgs/{name}/hooks/{hookId}/pings",
        null);
      return Fetch<bool>(request);
    }

    public Task<GitHubResponse<bool>> PingRepoWebhook(string repoFullname, long hookId) {
      var request = new GitHubRequest<object>(
        new HttpMethod("POST"),
        $"/repos/{repoFullname}/hooks/{hookId}/pings",
        null);
      return Fetch<bool>(request);
    }
  }
}
