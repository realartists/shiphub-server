namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Threading.Tasks;
  using DataModel;
  using DataModel.Types;

  /// <summary>
  /// This is a gross place to stuff a bunch of cache and rate limit logic.
  /// </summary>
  public class ShipHubFilter : IGitHubHandler {
    private IGitHubHandler _next;

    public ShipHubFilter(IGitHubHandler next) {
      _next = next;
    }

    public async Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request) {
      await HandleRequest(client, request);
      var response = await _next.Fetch<T>(client, request);
      await HandleResponse(response);
      return response;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "ShipHubFilter")]
    public Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector) {
      throw new NotSupportedException($"{nameof(ShipHubFilter)} only supports single fetches.");
    }

    private async Task HandleRequest(GitHubClient client, GitHubRequest request) {
      if (request.CacheOptions != null || request.Method != HttpMethod.Get) {
        return;
      }

      using (var context = new ShipHubContext()) {
        // TODO: Fancy cute logic to re-use other matching cache entries.
        var key = request.Uri.ToString();
        var cacheOptions = await context.CacheMetadata
          .AsNoTracking()
          .Where(x => x.AccessToken == client.DefaultToken)
          .Where(x => x.Key == key)
          .SingleOrDefaultAsync();
        request.CacheOptions = cacheOptions?.Metadata;
      }
    }

    private async Task HandleResponse(GitHubResponse response) {
      using (var context = new ShipHubContext()) {
        if (response.Status == HttpStatusCode.OK
          && response.Request.Method == HttpMethod.Get
          && response.Request.CacheOptions != GitHubCacheDetails.Empty) {
          // Update rate and cache
          await context.UpdateRateAndCache(response.RateLimit, response.Request.Uri.ToString(), GitHubMetadata.FromResponse(response));
        } else if (response.RateLimit != null) {
          // Rate only
          await context.UpdateRateLimit(response.RateLimit);
        }
      }
    }
  }
}
