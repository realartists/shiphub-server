namespace RealArtists.ShipHub.Actors.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Net;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Threading;
  using System.Threading.Tasks;
  using ActorInterfaces.GitHub;
  using Common.GitHub;
  using Newtonsoft.Json.Linq;
  using Orleans;
  using Common;
  using gh = Common.GitHub.Models;
  using RealArtists.ShipHub.ActorInterfaces;
  using System.Linq;

  /// <summary>
  /// This actor is for actions performed as the app itself, not any
  /// specific installation.
  /// </summary>
  public class GitHubInstallationActor : Grain, IInstallationActor, IGitHubClient {
    public const int PageSize = 100;
    private const string GitHubAppAccept = "application/vnd.github.machine-man-preview+json";

    // Should be less than Orleans timeout.
    // If changing, may also need to update values in CreateGitHubHttpClient()
    public static readonly TimeSpan GitHubRequestTimeout = OrleansAzureClient.ResponseTimeout.Subtract(TimeSpan.FromSeconds(2));

    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private long _integrationId;

    public Uri ApiRoot { get; }
    public ProductInfoHeaderValue UserAgent { get; } = new ProductInfoHeaderValue(ApplicationName, ApplicationVersion);
    public string UserInfo => $"GitHubIntegration: {_integrationId}";
    public long UserId { get; } = -3;

    public string AccessToken { get; }

    private static GitHubHandler SharedHandler;
    private static void EnsureHandlerPipelineCreated(Uri apiRoot) {
      if (SharedHandler != null) {
        return;
      }

      // Set the maximum number of concurrent connections
      HttpUtilities.SetServicePointConnectionLimit(apiRoot);

      SharedHandler = new GitHubHandler();
    }

    public GitHubInstallationActor(IShipHubConfiguration configuration) {
      ApiRoot = configuration.GitHubApiRoot;
      EnsureHandlerPipelineCreated(ApiRoot);
    }

    public override Task OnActivateAsync() {
      _integrationId = this.GetPrimaryKeyLong();
      return base.OnActivateAsync();
    }

    ////////////////////////////////////////////////////////////
    // Helpers
    ////////////////////////////////////////////////////////////

    private int _requestId = 0;
    public int NextRequestId() {
      return Interlocked.Increment(ref _requestId);
    }

    ////////////////////////////////////////////////////////////
    // Actor Methods
    ////////////////////////////////////////////////////////////

    public Task UpdateAccessibleRepos() {
      throw new NotImplementedException();
    }


    ////////////////////////////////////////////////////////////
    // GitHub Actions
    ////////////////////////////////////////////////////////////

    public Task<GitHubResponse<IEnumerable<gh.Repository>>> Repositories(GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"installation/repositories", cacheOptions, priority) {
        AcceptHeaderOverride = GitHubAppAccept,
      };
      return Fetch<IEnumerable<gh.Repository>>(request);
    }

    public Task<GitHubResponse<IDictionary<string, JToken>>> BranchProtection(string repoFullName, string branchName, GitHubCacheDetails cacheOptions, RequestPriority priority) {
      var request = new GitHubRequest($"repos/{repoFullName}/branches/{WebUtility.UrlEncode(branchName)}/protection") {
        AcceptHeaderOverride = GitHubAppAccept,
      };
      return Fetch<IDictionary<string, JToken>>(request);
    }

    ////////////////////////////////////////////////////////////
    // HTTP Helpers
    ////////////////////////////////////////////////////////////

    private Task<GitHubResponse<T>> Fetch<T>(GitHubRequest request) {
      var totalWait = Stopwatch.StartNew();
      try {
        using (var timeout = new CancellationTokenSource(GitHubRequestTimeout)) {
          return SharedHandler.Fetch<T>(this, request, timeout.Token);
        }
      } catch (TaskCanceledException exception) {
        totalWait.Stop();
        exception.Report($"GitHub Request Timeout after {totalWait.ElapsedMilliseconds}ms for [{request.Uri}]");
        throw;
      }
    }

    private async Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubRequest request, Func<T, TKey> keySelector, uint softPageLimit = uint.MaxValue, uint skipPages = 0, uint hardPageLimit = uint.MaxValue) {
      if (request.Method != HttpMethod.Get) {
        throw new InvalidOperationException("Only GETs can be paginated.");
      }
      if (softPageLimit == 0) {
        throw new InvalidOperationException($"{nameof(softPageLimit)} must be omitted or greater than 0");
      }
      if (hardPageLimit == 0) {
        throw new InvalidOperationException($"{nameof(hardPageLimit)} must be omitted or greater than 0");
      }

      // Always request the largest page size
      if (!request.Parameters.ContainsKey("per_page")) {
        request.AddParameter("per_page", PageSize);
      }

      // In all cases we need the first page 🙄
      var response = await Fetch<IEnumerable<T>>(request);

      // Save first page cache data
      var dangerousFirstPageCacheData = response.CacheData;

      // When successful, try to enumerate. Else immediately return the error.
      if (response.IsOk) {
        // If skipping pages, calculate here.
        switch (skipPages) {
          case 0:
            break;
          case 1 when response.Pagination?.Next != null:
            var nextUri = response.Pagination.Next;
            response = await Fetch<IEnumerable<T>>(response.Request.CloneWithNewUri(nextUri));
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
              response = await Fetch<IEnumerable<T>>(response.Request.CloneWithNewUri(nextUri));
            }
            break;
        }

        // Check hard limit
        if (response.Pagination?.CanInterpolate == true
          && response.Pagination.Interpolate().Count() > hardPageLimit) {
          // We'll hit our hard limit, so return no results.
          response.Pagination = null;
          response.Result = Array.Empty<T>();

          // Maybe this is a bad idea, but the goal with the hard limit is to cache the empty result we'll never be able to really enumerate.
          response.CacheData = dangerousFirstPageCacheData;
        } else if (softPageLimit > 1 && response.Pagination?.Next != null) {
          // Now, if there's more to do, enumerate the results
          // By default, upgrade background => subrequest
          var subRequestPriority = RequestPriority.SubRequest;
          // Ensure interactive => interactive
          if (response.Request.Priority == RequestPriority.Interactive) {
            subRequestPriority = RequestPriority.Interactive;
          }

          // Walk in order
          response = await EnumerateSequential(response, subRequestPriority, softPageLimit);
        }
      }

      // Response should have:
      // 1) Pagination header from last page
      // 2) Cache data from first page, IIF it's a complete result, and not truncated due to errors.
      // 3) Number of pages returned

      // Set first page cache data
      response.DangerousFirstPageCacheData = dangerousFirstPageCacheData;

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
        current = await Fetch<IEnumerable<TItem>>(nextReq);

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
