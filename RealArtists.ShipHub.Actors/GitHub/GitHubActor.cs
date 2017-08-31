namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Diagnostics;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common;
  using Common.GitHub;
  using Common.GitHub.Models;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using Orleans.Concurrency;
  using QueueClient;
  using dm = Common.DataModel;
  using dmt = Common.DataModel.Types;
  using gql = Common.GitHub.Models.GraphQL;

  public interface IGitHubClient {
    string AccessToken { get; }
    Uri ApiRoot { get; }
    ProductInfoHeaderValue UserAgent { get; }
    string UserInfo { get; }
    long UserId { get; }

    int NextRequestId();
  }

  [Reentrant]
  public class GitHubActor : Grain, IGitHubActor, IGitHubClient {
    public const int PageSize = 100;

    // Should be less than Orleans timeout.
    // If changing, may also need to update values in CreateGitHubHttpClient()
    public static readonly TimeSpan GitHubRequestTimeout = OrleansAzureClient.ResponseTimeout.Subtract(TimeSpan.FromSeconds(2));

    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private IFactory<dm.ShipHubContext> _shipContextFactory;
    private IShipHubQueueClient _queueClient;
    private IShipHubConfiguration _configuration;

    public string AccessToken { get; private set; }
    public string Login { get; private set; }
    public long UserId { get; private set; }

    private DateTimeOffset? _dropRequestsUntil;
    private volatile bool _dropRequestAbuse;
    private volatile bool _lastRequestLimited;
    private volatile GitHubRateLimit _rateLimit; // Do not update directly please.

    public Uri ApiRoot { get; }
    public ProductInfoHeaderValue UserAgent { get; } = new ProductInfoHeaderValue(ApplicationName, ApplicationVersion);
    public string UserInfo => $"{UserId} {Login}";

    private static IGitHubHandler SharedHandler;
    private static void EnsureHandlerPipelineCreated(Uri apiRoot) {
      if (SharedHandler != null) {
        return;
      }

      // Set the maximum number of concurrent connections
      HttpUtilities.SetServicePointConnectionLimit(apiRoot);

      SharedHandler = new GitHubHandler();
    }

    public GitHubActor(IFactory<dm.ShipHubContext> shipContextFactory, IShipHubQueueClient queueClient, IShipHubConfiguration configuration) {
      _shipContextFactory = shipContextFactory;
      _queueClient = queueClient;
      _configuration = configuration;

      ApiRoot = _configuration.GitHubApiRoot;
      EnsureHandlerPipelineCreated(ApiRoot);
    }

    public override async Task OnActivateAsync() {
      UserId = this.GetPrimaryKeyLong();

      using (var context = _shipContextFactory.CreateInstance()) {
        // Require user and token to already exist in database.
        var user = await context.Users
          .Include(x => x.Tokens)
          .SingleOrDefaultAsync(x => x.Id == UserId);

        if (user == null) {
          throw new InvalidOperationException($"User {UserId} does not exist.");
        }

        if (!user.Tokens.Any()) {
          throw new InvalidOperationException($"User {UserId} has no token.");
        }

        AccessToken = user.Tokens.First().Token;
        Login = user.Login;

        if (user.RateLimitReset != EpochUtility.EpochOffset) {
          UpdateRateLimit(new GitHubRateLimit(
            AccessToken,
            user.RateLimit,
            user.RateLimitRemaining,
            user.RateLimitReset));
        }
      }

      await base.OnActivateAsync();
    }

    public override async Task OnDeactivateAsync() {
      using (var context = _shipContextFactory.CreateInstance()) {
        await context.UpdateRateLimit(_rateLimit);
      }

      await base.OnDeactivateAsync();
    }

    ////////////////////////////////////////////////////////////
    // Helpers
    ////////////////////////////////////////////////////////////

    private int _requestId = 0;
    public int NextRequestId() {
      return Interlocked.Increment(ref _requestId);
    }

    private void UpdateRateLimit(GitHubRateLimit rateLimit) {
      lock (this) {
        if (_rateLimit == null
          || _rateLimit.Reset < rateLimit.Reset
          || _rateLimit.Remaining > rateLimit.Remaining) {
          _rateLimit = rateLimit;
        }
      }
    }

    ////////////////////////////////////////////////////////////
    // GitHub Actions
    ////////////////////////////////////////////////////////////

    public Task<GitHubResponse<IEnumerable<CommitComment>>> CommitComments(string repoFullName, string reference, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/commits/{WebUtility.UrlEncode(reference)}/comments", cacheOptions, priority);
      return FetchPaged(request, (CommitComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> CommitCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/comments/{commentId}/reactions", cacheOptions, priority) {
        // https://developer.github.com/v3/reactions/#list-reactions-for-a-commit-comment
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<CommitStatus>>> CommitStatuses(string repoFullName, string reference, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/commits/{WebUtility.UrlEncode(reference)}/statuses", cacheOptions, priority);
      return FetchPaged(request, (CommitStatus x) => x.Id);
    }

    public Task<GitHubResponse<IDictionary<string, JToken>>> BranchProtection(string repoFullName, string branchName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/branches/{WebUtility.UrlEncode(branchName)}/protection") {
        AcceptHeaderOverride = "application/vnd.github.loki-preview+json"
      };

      return EnqueueRequest<IDictionary<string, JToken>>(request);
    }

    public Task<GitHubResponse<Account>> User(string login, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"users/{login}", cacheOptions, priority);
      return EnqueueRequest<Account>(request);
    }

    public Task<GitHubResponse<Account>> User(long id, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"user/{id}", cacheOptions, priority);
      return EnqueueRequest<Account>(request);
    }

    public Task<GitHubResponse<Account>> User(GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest("user", cacheOptions, priority);
      return EnqueueRequest<Account>(request);
    }

    public Task<GitHubResponse<IEnumerable<UserEmail>>> UserEmails(GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest("user/emails", cacheOptions, priority);
      return FetchPaged(request, (UserEmail x) => x.Email);
    }

    public Task<GitHubResponse<Repository>> Repository(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"/repos/{repoFullName}", cacheOptions, priority);
      return EnqueueRequest<Repository>(request);
    }

    public Task<GitHubResponse<Repository>> Repository(long repoId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"/repositories/{repoId}", cacheOptions, priority);
      return EnqueueRequest<Repository>(request);
    }

    public Task<GitHubResponse<IEnumerable<Repository>>> Repositories(GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest("user/repos", cacheOptions, priority);
      return FetchPaged(request, (Repository x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Timeline(string repoFullName, int issueNumber, long issueId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/timeline", cacheOptions, priority) {
        // Timeline support (application/vnd.github.mockingbird-preview+json)
        // https://developer.github.com/changes/2016-05-23-timeline-preview-api/
        AcceptHeaderOverride = "application/vnd.github.mockingbird-preview+json",
      };
      // Ugh. Uniqueness here is hard because IDs aren't.
      return FetchPaged(request, (IssueEvent x) => {
        // Fixup since GitHub doesn't consistently send it
        x.FallbackIssueId = issueId;
        return x.UniqueKey;
      });
    }

    public Task<GitHubResponse<Issue>> Issue(string repoFullName, int number, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{number}", cacheOptions, priority) {
        // https://developer.github.com/v3/issues/#reactions-summary 
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      return EnqueueRequest<Issue>(request);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> Issues(string repoFullName, DateTimeOffset since, uint maxPages, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues", cacheOptions, priority) {
        // https://developer.github.com/v3/issues/#reactions-summary 
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      }
        .AddParameter("state", "all")
        .AddParameter("sort", "updated")
        .AddParameter("direction", "asc")
        .AddParameter("since", since);

      return FetchPaged(request, (Issue x) => x.Id, maxPages);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> NewestIssues(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues", cacheOptions, priority) {
        // https://developer.github.com/v3/issues/#reactions-summary 
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      request.AddParameter("state", "all");
      request.AddParameter("sort", "created");
      request.AddParameter("direction", "desc");
      request.AddParameter("per_page", PageSize);

      return EnqueueRequest<IEnumerable<Issue>>(request);
    }

    public Task<GitHubResponse<IEnumerable<Issue>>> IssueMentions(DateTimeOffset? since, uint maxPages, GitHubCacheDetails cacheOptions = null, RequestPriority priority = RequestPriority.Background) {
      var request = new GitHubRequest($"/issues", cacheOptions, priority)
        .AddParameter("state", "all")
        .AddParameter("sort", "updated")
        .AddParameter("direction", "asc")
        .AddParameter("filter", "mentioned")
        .AddParameter("since", since);

      return FetchPaged(request, (Issue x) => x.Id, maxPages);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueReactions(string repoFullName, int issueNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/reactions", cacheOptions, priority) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> IssueCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments/{commentId}/reactions", cacheOptions, priority) {
        // Reactions are in beta
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json",
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IssueComment>> IssueComment(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments/{commentId}", cacheOptions, priority);
      return EnqueueRequest<IssueComment>(request);
    }

    public Task<GitHubResponse<IEnumerable<IssueComment>>> IssueComments(string repoFullName, int issueNumber, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/{issueNumber}/comments", cacheOptions, priority);
      request.AddParameter("since", since);
      return FetchPaged(request, (IssueComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<IssueComment>>> IssueComments(string repoFullName, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments", cacheOptions, priority)
        .AddParameter("since", since)
        .AddParameter("sort", "updated")
        .AddParameter("direction", "asc");

      return FetchPaged(request, (IssueComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<IssueComment>>> Comments(string repoFullName, DateTimeOffset since, uint maxPages, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/comments", cacheOptions, priority)
        .AddParameter("sort", "updated")
        .AddParameter("direction", "asc")
        .AddParameter("since", since);

      return FetchPaged(request, (IssueComment x) => x.Id, maxPages);
    }

    public Task<GitHubResponse<Commit>> Commit(string repoFullName, string hash, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/commits/{hash}", cacheOptions, priority);
      return EnqueueRequest<Commit>(request);
    }

    public Task<GitHubResponse<IEnumerable<IssueEvent>>> Events(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/issues/events", cacheOptions, priority);
      request.AddParameter("sort", "updated");
      request.AddParameter("direction", "asc");
      return FetchPaged(request, (IssueEvent x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Label>>> Labels(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/labels", cacheOptions, priority);
      return FetchPaged(request, (Label x) => Tuple.Create(x.Name, x.Color));
    }

    public Task<GitHubResponse<IEnumerable<Milestone>>> Milestones(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/milestones", cacheOptions, priority);
      request.AddParameter("state", "all");
      return FetchPaged(request, (Milestone x) => x.Id);
    }

    public Task<GitHubResponse<Account>> Organization(string login, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"orgs/{login}", cacheOptions, priority);
      return EnqueueRequest<Account>(request);
    }

    public Task<GitHubResponse<Account>> Organization(long id, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"organizations/{id}", cacheOptions, priority);
      return EnqueueRequest<Account>(request);
    }

    public async Task<GitHubResponse<IEnumerable<OrganizationMembership>>> OrganizationMemberships(string state, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest("user/memberships/orgs", cacheOptions, priority);
      request.AddParameter(nameof(state), state);
      var result = await FetchPaged(request, (OrganizationMembership x) => x.Organization.Id);

      if (result.IsOk) {
        // Seriously GitHub?
        foreach (var membership in result.Result) {
          membership.Organization.Type = GitHubAccountType.Organization;
        }
      }

      return result;
    }

    public Task<GitHubResponse<IEnumerable<Account>>> OrganizationMembers(string orgLogin, string role, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      // defaults: filter=all, role=all
      var request = new GitHubRequest($"orgs/{orgLogin}/members", cacheOptions, priority);
      request.AddParameter("role", role);
      return FetchPaged(request, (Account x) => x.Id);
    }

    public Task<GitHubResponse<PullRequest>> PullRequest(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/{pullRequestNumber}", cacheOptions, priority);
      return EnqueueRequest<PullRequest>(request);
    }

    public Task<GitHubResponse<IEnumerable<PullRequest>>> PullRequests(string repoFullName, string sort, string direction, uint skipPages, uint maxPages, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls", cacheOptions, priority);
      request.AddParameter("state", "all");
      request.AddParameter("sort", sort);
      request.AddParameter("direction", direction);
      return FetchPaged(request, (PullRequest x) => x.Id, maxPages, skipPages);
    }

    public Task<GitHubResponse<PullRequestComment>> PullRequestComment(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/comments/{commentId}", cacheOptions, priority);
      return EnqueueRequest<PullRequestComment>(request);
    }

    public Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestComments(string repoFullName, DateTimeOffset? since, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/comments", cacheOptions, priority)
        .AddParameter("since", since)
        .AddParameter("sort", "updated")
        .AddParameter("direction", "asc");
      return FetchPaged(request, (PullRequestComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestComments(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/{pullRequestNumber}/comments", cacheOptions, priority);
      return FetchPaged(request, (PullRequestComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Reaction>>> PullRequestCommentReactions(string repoFullName, long commentId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/comments/{commentId}/reactions", cacheOptions, priority) {
        // https://developer.github.com/v3/reactions/#list-reactions-for-a-pull-request-review-comment
        AcceptHeaderOverride = "application/vnd.github.squirrel-girl-preview+json"
      };
      return FetchPaged(request, (Reaction x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Review>>> PullRequestReviews(string repoFullName, int pullRequestNumber, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/{pullRequestNumber}/reviews", cacheOptions, priority);
      return FetchPaged(request, (Review x) => x.Id);
    }

    public async Task<GitHubResponse<IEnumerable<PullRequestReviewResult>>> PullRequestReviews(string repoFullName, IEnumerable<int> pullRequestNumbers, RequestPriority priority) {
      var resources = pullRequestNumbers
        .Select(x => $"  _{x}: resource(url: \"https://github.com/{repoFullName}/pull/{x}\") {{ ...PullRequestInfo }}\r\n")
        .ToArray();
      var query = "query { rateLimit { cost nodeCount limit remaining resetAt }\r\n"
        + string.Join("\r\n", resources)
        + @"}
          fragment PullRequestInfo on PullRequest {
            databaseId
            reviews(first: 100) {
              pageInfo { hasNextPage }
              nodes {
                databaseId
                author {
                  __typename
                  login
                  ... on User { databaseId name }
                  ... on Organization { databaseId  name }
                  ... on Bot { databaseId }
                }
                body
                commit { oid }
                state
                submittedAt
              }
            }
          }";
      var request = new GitHubGraphQLRequest(query, priority);
      var response = await EnqueueRequest<JObject>(request);

      // I hate this
      var copyResponse = new GitHubResponse<IEnumerable<PullRequestReviewResult>>(request) {
        Date = response.Date,
        Error = response.Error,
        Redirect = response.Redirect,
        Status = response.Status,
        RateLimit = response.RateLimit,
      };

      // Parse!
      if (response.IsOk) {
        var data = response.Result;
        var issueReviews = new List<PullRequestReviewResult>();

        foreach (var prop in data) {
          if (prop.Key == "rateLimit") {
            var rate = prop.Value.ToObject<gql.RateLimit>(GraphQLSerialization.JsonSerializer);
            copyResponse.RateLimit = new GitHubRateLimit(response.RateLimit.AccessToken, rate.Limit, rate.Remaining, rate.ResetAt);
          } else {
            var pr = prop.Value.ToObject<gql.PullRequest>(GraphQLSerialization.JsonSerializer);
            if (pr == null) { continue; } // Sometimes GitHub claims PRs don't exist. It's good times.
            var reviews = new List<Review>();

            foreach (var review in pr.Reviews.Nodes) {
              if (review.State.Equals("PENDING", StringComparison.OrdinalIgnoreCase)) {
                // Drop pending reviews from these responses.
                // TODO: If possible, safely incorporate them.
                continue;
              }

              if (review.Author == null) {
                review.Author = gql.User.Ghost;
              }

              reviews.Add(new Review() {
                Body = review.Body,
                CommitId = review.Commit?.Id,
                Id = review.Id,
                State = review.State,
                SubmittedAt = review.SubmittedAt,
                User = new Account() {
                  Id = review.Author.Id,
                  Login = review.Author.Login,
                  Name = review.Author.Name,
                  Type = review.Author.Type,
                }
              });
            }

            issueReviews.Add(new PullRequestReviewResult() {
              PullRequestId = pr.Id,
              Reviews = reviews,
              MoreResults = pr.Reviews.PageInfo.HasNextPage,
            });
          }
        }

        copyResponse.Result = issueReviews;
      }

      return copyResponse;
    }

    public Task<GitHubResponse<IEnumerable<PullRequestComment>>> PullRequestReviewComments(string repoFullName, int pullRequestNumber, long pullRequestReviewId, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/pulls/{pullRequestNumber}/reviews/{pullRequestReviewId}/comments", cacheOptions, priority);
      return FetchPaged(request, (PullRequestComment x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Account>>> Assignable(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/assignees", cacheOptions, priority);
      return FetchPaged(request, (Account x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> OrganizationWebhooks(string name, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"orgs/{name}/hooks", cacheOptions, priority);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<IEnumerable<Webhook>>> RepositoryWebhooks(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/hooks", cacheOptions, priority);
      return FetchPaged(request, (Webhook x) => x.Id);
    }

    public Task<GitHubResponse<Webhook>> AddRepositoryWebhook(string repoFullName, Webhook hook, RequestPriority priority) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"repos/{repoFullName}/hooks", hook, priority);
      return EnqueueRequest<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> AddOrganizationWebhook(string orgName, Webhook hook, RequestPriority priority) {
      var request = new GitHubRequest<Webhook>(HttpMethod.Post, $"orgs/{orgName}/hooks", hook, priority);
      return EnqueueRequest<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> DeleteRepositoryWebhook(string repoFullName, long hookId, RequestPriority priority) {
      var request = new GitHubRequest(HttpMethod.Delete, $"repos/{repoFullName}/hooks/{hookId}", priority);
      return EnqueueRequest<bool>(request);
    }

    public Task<GitHubResponse<bool>> DeleteOrganizationWebhook(string orgName, long hookId, RequestPriority priority) {
      var request = new GitHubRequest(HttpMethod.Delete, $"orgs/{orgName}/hooks/{hookId}", priority);
      return EnqueueRequest<bool>(request);
    }

    public Task<GitHubResponse<Webhook>> EditRepositoryWebhookEvents(string repoFullName, long hookId, IEnumerable<string> events, RequestPriority priority) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"repos/{repoFullName}/hooks/{hookId}",
        new {
          Events = events,
        },
        priority);

      return EnqueueRequest<Webhook>(request);
    }

    public Task<GitHubResponse<Webhook>> EditOrganizationWebhookEvents(string orgName, long hookId, IEnumerable<string> events, RequestPriority priority) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"orgs/{orgName}/hooks/{hookId}",
        new {
          Events = events,
        },
        priority);

      return EnqueueRequest<Webhook>(request);
    }

    public Task<GitHubResponse<bool>> PingOrganizationWebhook(string name, long hookId, RequestPriority priority) {
      var request = new GitHubRequest(HttpMethod.Post, $"orgs/{name}/hooks/{hookId}/pings", priority);
      return EnqueueRequest<bool>(request);
    }

    public Task<GitHubResponse<bool>> PingRepositoryWebhook(string repoFullName, long hookId, RequestPriority priority) {
      var request = new GitHubRequest(HttpMethod.Post, $"repos/{repoFullName}/hooks/{hookId}/pings", priority);
      return EnqueueRequest<bool>(request);
    }

    public Task<GitHubResponse<IEnumerable<ContentsFile>>> ListDirectoryContents(string repoFullName, string directoryPath, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      if (!directoryPath.StartsWith("/", StringComparison.Ordinal)) {
        directoryPath = "/" + directoryPath;
      }
      var request = new GitHubRequest($"repos/{repoFullName}/contents{directoryPath}", cacheOptions, priority);
      return FetchPaged(request, (ContentsFile f) => f.Path);
    }

    public Task<GitHubResponse<byte[]>> FileContents(string repoFullName, string filePath, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      if (!filePath.StartsWith("/", StringComparison.Ordinal)) {
        filePath = "/" + filePath;
      }
      var request = new GitHubRequest($"repos/{repoFullName}/contents{filePath}", cacheOptions, priority);
      return EnqueueRequest<byte[]>(request);
    }

    private Task<GitHubResponse<IEnumerable<Project>>> Projects(string endpoint, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest(endpoint, cacheOptions, priority) {
        AcceptHeaderOverride = "application/vnd.github.inertia-preview+json",
      };
      return FetchPaged(request, (Project p) => p.Id);
    }

    public Task<GitHubResponse<IEnumerable<Project>>> RepositoryProjects(string repoFullName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return Projects($"repos/{repoFullName}/projects", cacheOptions, priority);
    }

    public Task<GitHubResponse<IEnumerable<Project>>> OrganizationProjects(string organizationLogin, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      return Projects($"orgs/{organizationLogin}/projects", cacheOptions, priority);
    }

    public Task<GitHubResponse<PullRequest>> CreatePullRequest(string repoFullName, string title, string body, string baseSha, string headSha, RequestPriority priority) {
      var prBody = new JObject() {
        { "title", title},
        { "base", baseSha},
        { "head", headSha},
      };

      // GitHub gets upset if you send null or empty string for body.
      if (!body.IsNullOrWhiteSpace()) {
        prBody.Add("body", body);
      }

      var request = new GitHubRequest<JObject>(HttpMethod.Post, $"repos/{repoFullName}/pulls", prBody, priority);
      return EnqueueRequest<PullRequest>(request);
    }

    public Task<GitHubResponse<Issue>> UpdateIssue(string repoFullName, int number, int? milestone, IEnumerable<string> assignees, IEnumerable<string> labels, RequestPriority priority) {
      var request = new GitHubRequest<object>(
        new HttpMethod("PATCH"),
        $"repos/{repoFullName}/issues/{number}",
        new {
          Assignees = assignees,
          Labels = labels,
          Milestone = milestone,
        },
        priority);

      return EnqueueRequest<Issue>(request);
    }

    ////////////////////////////////////////////////////////////
    // Handling
    ////////////////////////////////////////////////////////////

    private object _queueLock = new object();
    private Task _queueProcessor;
    private ConcurrentQueue<FutureRequest> _backgroundQueue = new ConcurrentQueue<FutureRequest>();
    private ConcurrentQueue<FutureRequest> _subRequestQueue = new ConcurrentQueue<FutureRequest>();
    private ConcurrentQueue<FutureRequest> _interactiveQueue = new ConcurrentQueue<FutureRequest>();

    private class FutureRequest {
      public FutureRequest(Func<Task> requestFunc, Action cancelAction) {
        MakeRequest = requestFunc;
        Cancel = cancelAction;
      }

      public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
      public Func<Task> MakeRequest { get; }
      public Action Cancel { get; }
    }

    private Task<GitHubResponse<T>> EnqueueRequest<T>(GitHubRequest request) {
      var completionTask = new TaskCompletionSource<GitHubResponse<T>>();
      Func<Task> executeRequestTask = async () => {
        try {
          /* ALL RATE LIMIT ENFORCEMENT SHOULD EXIST HERE ONLY
           * We need one central place for this to maintain sanity.
           * 
           * So when we *first* hit a rate limit or abuse warning, log the exception.
           * Then throw but don't log until a request succeeded. Upstream callers are
           * expected to act (if needed) on the exceptions, but silently swallow them.
           * 
           * This should ensure we're aware of all rate and abuse issues, without
           * overwhelming us with non-actionable exceptions.
           */

          if (_dropRequestsUntil != null && _dropRequestsUntil.Value > DateTimeOffset.UtcNow) {
            var rateException = new GitHubRateException(UserInfo, request.Uri, _rateLimit, _dropRequestAbuse);
            if (!_lastRequestLimited) {
              _lastRequestLimited = true;
              rateException.Report(userInfo: UserInfo);
            }
            throw rateException;
          }

          var totalWait = Stopwatch.StartNew();
          try {
            GitHubResponse<T> result;
            using (var timeout = new CancellationTokenSource(GitHubRequestTimeout)) {
              result = await SharedHandler.Fetch<T>(this, request, timeout.Token);
            }
            await PostProcessResponse(result);
            completionTask.SetResult(result);
          } catch (TaskCanceledException exception) {
            totalWait.Stop();
            exception.Report($"GitHub Request Timeout after {totalWait.ElapsedMilliseconds}ms for [{request.Uri}]");
            throw;
          }
        } catch (Exception e) {
          completionTask.SetException(e);
        }
      };

      // Enqueue the request
      var futureRequest = new FutureRequest(executeRequestTask, completionTask.SetCanceled);
      lock (_queueLock) {
        switch (request.Priority) {
          case RequestPriority.Interactive:
            _interactiveQueue.Enqueue(futureRequest);
            break;
          case RequestPriority.SubRequest:
            _subRequestQueue.Enqueue(futureRequest);
            break;
          case RequestPriority.Background:
          case RequestPriority.Unspecified:
          default:
            _backgroundQueue.Enqueue(futureRequest);
            break;
        }

        // Start dispatcher if needed.
        if (_queueProcessor == null) {
          _queueProcessor = DispatchRequests();
          _queueProcessor.LogFailure(UserInfo);
        }
      }

      return completionTask.Task;
    }

    private async Task DispatchRequests() {
      Func<Task> nextRequest;

      while (true) {
        try {
          lock (_queueLock) {
            nextRequest = DequeueNextRequest();
            if (nextRequest == null) {
              _queueProcessor = null;
              return;
            }
          }

          await nextRequest();
        } catch (Exception e) {
          e.Report(userInfo: UserInfo);
        }
      }

      Func<Task> DequeueNextRequest() {
        // Get next request by priority
        if (_interactiveQueue.TryDequeue(out var dequeued)
          || _subRequestQueue.TryDequeue(out dequeued)
          || _backgroundQueue.TryDequeue(out dequeued)) {
          var delay = DateTimeOffset.UtcNow.Subtract(dequeued.Timestamp);
          if (delay.TotalSeconds > 30) {
            var backlog = _interactiveQueue.Count + _subRequestQueue.Count + _backgroundQueue.Count;
            if (delay.TotalMinutes < 3) {
              Log.Info($"[{UserInfo}] Request delayed {delay} with backlog {backlog}");
            } else {
              Log.Error($"[{UserInfo}] Request delayed {delay} with backlog {backlog}. CANCELLING!");
              dequeued.Cancel();
              return DequeueNextRequest();
            }
          }

          return dequeued.MakeRequest;
        }

        return null;
      }
    }

    private async Task PostProcessResponse(GitHubResponse response) {
      // Token revocation handling and abuse.
      var abuse = false;
      DateTimeOffset? limitUntil = null;
      if (response?.Status == HttpStatusCode.Unauthorized) {
        using (var ctx = _shipContextFactory.CreateInstance()) {
          var changes = await ctx.RevokeAccessTokens(UserId);
          await _queueClient.NotifyChanges(changes);
        }
        DeactivateOnIdle();
      } else if (response.Error?.IsAbuse == true) {
        abuse = true;
        limitUntil = response.RetryAfter ?? DateTimeOffset.UtcNow.AddSeconds(60); // Default to 60 seconds.
      } else if (_rateLimit?.IsExceeded == true) {
        limitUntil = _rateLimit.Reset;
      }

      if (limitUntil != null) {
        // Don't set _lastRequestLimited until an error is logged
        _dropRequestAbuse = abuse;
        _dropRequestsUntil = limitUntil;
        using (var context = _shipContextFactory.CreateInstance()) {
          var oldRate = _rateLimit;
          var newRate = new GitHubRateLimit(
            oldRate.AccessToken,
            oldRate.Limit,
            Math.Min(oldRate.Remaining, GitHubRateLimit.RateLimitFloor - 1),
            limitUntil.Value);
          UpdateRateLimit(newRate);

          // Record in DB for sync notification
          await context.UpdateRateLimit(newRate);
        }

        // Force sync notification
        var changes = new dmt.ChangeSummary();
        changes.Users.Add(UserId);
        await _queueClient.NotifyChanges(changes);
      } else if (response.RateLimit != null) {
        _lastRequestLimited = false;
        // Normal rate limit tracking
        UpdateRateLimit(response.RateLimit);
      }
    }

    private async Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubRequest request, Func<T, TKey> keySelector, uint maxPages = uint.MaxValue, uint skipPages = 0) {
      if (request.Method != HttpMethod.Get) {
        throw new InvalidOperationException("Only GETs can be paginated.");
      }
      if (maxPages == 0) {
        throw new InvalidOperationException($"{nameof(maxPages)} must be omitted or greater than 0");
      }

      // Always request the largest page size
      if (!request.Parameters.ContainsKey("per_page")) {
        request.AddParameter("per_page", PageSize);
      }

      // In all cases we need the first page 🙄
      var response = await EnqueueRequest<IEnumerable<T>>(request);

      // When successful, try to enumerate. Else immediately return the error.
      if (response.IsOk) {
        // If skipping pages, calculate here.
        switch (skipPages) {
          case 0:
            break;
          case 1 when response.Pagination?.Next != null:
            var nextUri = response.Pagination.Next;
            response = await EnqueueRequest<IEnumerable<T>>(response.Request.CloneWithNewUri(nextUri));
            break;
          case 1: // response.Pagination == null
            response.Result = Array.Empty<T>();
            break;
          default: // skipPages > 1
            if (response.Pagination?.CanInterpolate != true) {
              throw new InvalidOperationException($"Skipping pages is not supported for [{response.Request.Uri}]: {response.Pagination?.SerializeObject()}");
            }
            nextUri = response.Pagination.Interpolate().Skip((int)(skipPages - 1)).FirstOrDefault();
            if (nextUri == null) {
              // We skipped more pages than existed.
              response.Pagination = null;
              response.Result = Array.Empty<T>();
            } else {
              response = await EnqueueRequest<IEnumerable<T>>(response.Request.CloneWithNewUri(nextUri));
            }
            break;
        }

        // Now, if there's more to do, enumerate the results
        if (maxPages > 1 && response.Pagination?.Next != null) {
          // By default, upgrade background => subrequest
          var subRequestPriority = RequestPriority.SubRequest;
          // Ensure interactive => interactive
          if (response.Request.Priority == RequestPriority.Interactive) {
            subRequestPriority = RequestPriority.Interactive;
          }

          // Walk in order
          response = await EnumerateSequential(response, subRequestPriority, maxPages);
        }
      }

      // Response should have:
      // 1) Pagination header from last page
      // 2) Cache data from first page, IIF it's a complete result, and not truncated due to errors.
      // 3) Number of pages returned

      return response.Distinct(keySelector);
    }

    private async Task<GitHubResponse<IEnumerable<TItem>>> EnumerateSequential<TItem>(GitHubResponse<IEnumerable<TItem>> firstPage, RequestPriority priority, uint maxPages) {
      var partial = false;
      var results = new List<TItem>(firstPage.Result);

      // Walks pages in order, one at a time.
      var current = firstPage;
      uint page = 1;

      while (current.Pagination?.Next != null) {
        if (page >= maxPages) {
          partial = true;
          break;
        }

        var nextReq = current.Request.CloneWithNewUri(current.Pagination.Next);
        nextReq.Priority = priority;
        current = await EnqueueRequest<IEnumerable<TItem>>(nextReq);

        if (current.IsOk) {
          results.AddRange(current.Result);
        } else if (maxPages < uint.MaxValue) {
          // Return results up to this point.
          partial = true;
          break;
        } else {
          // On error, return the error
          return current;
        }

        ++page;
      }

      // Keep cache from the first page, rate + pagination from the last.
      var result = firstPage;
      result.Result = results;
      result.Pages = page;
      result.RateLimit = current.RateLimit;

      // Don't return cache data for partial results
      if (partial) {
        result.CacheData = null;
      }

      return result;
    }
  }
}
