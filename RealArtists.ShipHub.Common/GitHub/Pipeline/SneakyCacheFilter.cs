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
  /// This is a gross place to stuff hacky cache logic.
  /// Rate limit tracking has already moved to Orleans.
  /// It needs to go, and will with the Orleans spider transition.
  /// </summary>
  public class SneakyCacheFilter : IGitHubHandler {
    private IGitHubHandler _next;
    private IFactory<ShipHubContext> _shipContextFactory;

    public SneakyCacheFilter(IGitHubHandler next, IFactory<ShipHubContext> shipContextFactory) {
      _next = next;
      _shipContextFactory = shipContextFactory;
    }

    public async Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request) {
      await HandleRequest(client, request);
      var response = await _next.Fetch<T>(client, request);
      await HandleResponse(response);
      return response;
    }

    public Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector) {
      throw new NotSupportedException($"{nameof(SneakyCacheFilter)} only supports single fetches.");
    }

    private async Task HandleRequest(GitHubClient client, GitHubRequest request) {
      if (request.CacheOptions != null || request.Method != HttpMethod.Get) {
        return;
      }

      using (var context = _shipContextFactory.CreateInstance()) {
        // TODO: Fancy cute logic to re-use other matching cache entries.
        var key = request.Uri.ToString();
        var cacheOptions = await context.CacheMetadata
          .AsNoTracking()
          .Where(x => x.AccessToken == client.AccessToken)
          .Where(x => x.Key == key)
          .SingleOrDefaultAsync();

        if (cacheOptions != null) {
          // End goal is to handle caching explicitly and deprecate this handler.
          // To start, log when it's used.
          Log.Info($"Injecting sneaky cache data for ({client.UserInfo}) GET {key}");
          request.CacheOptions = cacheOptions.Metadata;
        }
      }
    }

    private async Task HandleResponse(GitHubResponse response) {
      if (response.IsOk
         && response.Request.Method == HttpMethod.Get
         && response.Request.CacheOptions?.AccessToken != null) {
        using (var context = _shipContextFactory.CreateInstance()) {
          await context.UpdateCache(response.Request.Uri.ToString(), GitHubMetadata.FromResponse(response));
        }
      }
    }
  }
}
