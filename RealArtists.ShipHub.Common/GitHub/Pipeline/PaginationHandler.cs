namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Threading;
  using System.Threading.Tasks;

  public class PaginationHandler : IGitHubHandler {
    public const int PerFetchConcurrencyLimit = 4;
    //public const int PageSize = 100;
    public const int PageSize = 10;
    public const bool InterpolationEnabled = true;

    private IGitHubHandler _next;

    public PaginationHandler(IGitHubHandler next) {
      _next = next;
    }

    public Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request) {
      // Pass through.
      return _next.Fetch<T>(client, request);
    }

    public async Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector, ushort? maxPages = null) {
      if (request.Method != HttpMethod.Get) {
        throw new InvalidOperationException("Only GETs are supported for pagination.");
      }

      // Always request the largest page size
      if (!request.Parameters.ContainsKey("per_page")) {
        request.AddParameter("per_page", PageSize);
      }

      // Fetch has the retry logic.
      var result = await _next.Fetch<IEnumerable<T>>(client, request);

      if (result.IsOk
        && result.Pagination != null
        && (maxPages == null || maxPages > 1)) {
        result = await EnumerateParallel<IEnumerable<T>, T>(client, result, maxPages);
      }

      return result.Distinct(keySelector);
    }

    private async Task<GitHubResponse<TCollection>> EnumerateParallel<TCollection, TItem>(GitHubClient client, GitHubResponse<TCollection> firstPage, ushort? maxPages)
      where TCollection : IEnumerable<TItem> {
      var results = new List<TItem>(firstPage.Result);
      IEnumerable<GitHubResponse<TCollection>> batch;
      var partial = false;

      // TODO: Cancellation (for when errors are encountered)?

      if (InterpolationEnabled && firstPage.Pagination?.CanInterpolate == true) {
        var pages = firstPage.Pagination.Interpolate();

        if (maxPages < pages.Count()) {
          partial = true;
          pages = pages.Take((int)maxPages - 1);
        }

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
          firstPage.Status = HttpStatusCode.Forbidden; // Rate Limited
          return firstPage;
        }

        batch = await Batch(pageRequestors);

        foreach (var response in batch) {
          if (response.IsOk) {
            results.AddRange(response.Result);
          } else if (maxPages != null) {
            // Return results up to this point.
            partial = true;
            break;
          } else {
            return response;
          }
        }
      } else { // Walk in order
        var current = firstPage;
        ushort page = 0;
        while (current.Pagination?.Next != null
          && (maxPages == null || page < maxPages)) {

          var nextReq = current.Request.CloneWithNewUri(current.Pagination.Next);
          current = await _next.Fetch<TCollection>(client, nextReq);

          if (current.IsOk) {
            results.AddRange(current.Result);
          } else if (maxPages != null) {
            // Return results up to this point.
            partial = true;
            break;
          } else {
            return current;
          }

          ++page;
        }
        // Just use the last request.
        batch = new[] { current };
      }

      // Keep cache and other headers from first page.
      var final = firstPage;
      final.Result = (TCollection)(IEnumerable<TItem>)results;

      // Clear cache data if partial result
      if (partial) {
        final.CacheData = null;
      }

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

    public async Task<IEnumerable<T>> Batch<T>(IEnumerable<Func<Task<T>>> batchTasks)
      where T : GitHubResponse {
      var tasks = new List<Task<T>>();
      var abort = false;
      using (var limit = new SemaphoreSlim(PerFetchConcurrencyLimit, PerFetchConcurrencyLimit)) {
        foreach (var item in batchTasks) {
          await limit.WaitAsync();

          if (abort) {
            break;
          }

          tasks.Add(Task.Run(async delegate {
            var response = await item();

            if (!response.Succeeded) {
              abort = true;
            }

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
