namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net.Http;
  using System.Threading;
  using System.Threading.Tasks;

  public class PaginationHandler : IGitHubHandler {
    public const int PerFetchConcurrencyLimit = 4;
    public const int PageSize = 100;
    public const bool InterpolationEnabled = true;

    private IGitHubHandler _next;

    public PaginationHandler(IGitHubHandler next) {
      _next = next;
    }

    public Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request) {
      // Pass through.
      return _next.Fetch<T>(client, request);
    }

    public async Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector) {
      if (request.Method != HttpMethod.Get) {
        throw new InvalidOperationException("Only GETs are supported for pagination.");
      }

      // Always request the largest page size
      if (!request.Parameters.ContainsKey("per_page")) {
        request.AddParameter("per_page", PageSize);
      }

      // Fetch has the retry logic.
      var result = await _next.Fetch<IEnumerable<T>>(client, request);

      if (!result.IsError && result.Pagination != null) {
        result = await EnumerateParallel<IEnumerable<T>, T>(client, result);
      }

      return result.Distinct(keySelector);
    }

    private async Task<GitHubResponse<TCollection>> EnumerateParallel<TCollection, TItem>(GitHubClient client, GitHubResponse<TCollection> firstPage)
      where TCollection : IEnumerable<TItem> {
      var results = new List<TItem>(firstPage.Result);
      IEnumerable<GitHubResponse<TCollection>> batch;

      // TODO: Cancellation (for when errors are encountered)?

      if (InterpolationEnabled && firstPage.Pagination?.CanInterpolate == true) {
        var pages = firstPage.Pagination.Interpolate();
        var pageRequestors = pages
          .Select(page => {
            Func<Task<GitHubResponse<TCollection>>> requestor = () => {
              var request = firstPage.Request.CloneWithNewUri(page);
              return Fetch<TCollection>(client, request);
            };

            return requestor;
          }).ToArray();


        // Check if we can request all the pages within the limit.
        if (firstPage.RateLimit.RateLimitRemaining < pageRequestors.Length) {
          firstPage.Result = default(TCollection);
          firstPage.IsError = true;
          firstPage.ErrorSeverity = GitHubErrorSeverity.RateLimited;
          return firstPage;
        }

        batch = await Batch(pageRequestors);

        foreach (var response in batch) {
          if (response.IsError) {
            return response;
          } else {
            results.AddRange(response.Result);
          }
        }
      } else { // Walk in order
        var current = firstPage;
        while (current.Pagination?.Next != null) {
          var nextReq = current.Request.CloneWithNewUri(current.Pagination.Next);
          current = await _next.Fetch<TCollection>(client, nextReq);

          if (current.IsError) {
            return current;
          } else {
            results.AddRange(current.Result);
          }
        }
        // Just use the last request.
        batch = new[] { current };
      }

      // Keep cache and other headers from first page.
      var final = firstPage;
      final.Result = (TCollection)(IEnumerable<TItem>)results;

      var rateLimit = final.RateLimit;
      foreach (var req in batch) {
        // Rate Limit
        var limit = req.RateLimit;
        if (limit.RateLimitReset > rateLimit.RateLimitReset) {
          rateLimit = limit;
        } else if (limit.RateLimitReset == rateLimit.RateLimitReset) {
          rateLimit.RateLimit = Math.Min(rateLimit.RateLimit, limit.RateLimit);
          rateLimit.RateLimitRemaining = Math.Min(rateLimit.RateLimitRemaining, limit.RateLimitRemaining);
        } // else ignore it
      }

      return final;
    }

    public async Task<IEnumerable<T>> Batch<T>(IEnumerable<Func<Task<T>>> batchTasks) {
      var tasks = new List<Task<T>>();
      using (var limit = new SemaphoreSlim(PerFetchConcurrencyLimit, PerFetchConcurrencyLimit)) {
        foreach (var item in batchTasks) {
          await limit.WaitAsync();

          tasks.Add(Task.Run(async delegate {
            var response = await item();
            limit.Release();
            return response;
          }));
        }

        await Task.WhenAll(tasks);

        var results = new List<T>();
        foreach (var task in tasks) {
          // we know they've completed.
          results.Add(task.Result);
        }

        return results;
      }
    }
  }
}
