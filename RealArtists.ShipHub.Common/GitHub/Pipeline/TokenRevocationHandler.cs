namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Collections.Generic;
  using System.Net;
  using System.Threading.Tasks;

  public class TokenRevocationHandler : IGitHubHandler {
    private IGitHubHandler _next;
    private Func<string, Task> _revoked;

    public TokenRevocationHandler(IGitHubHandler next, Func<string, Task> revoked) {
      _next = next;
      _revoked = revoked;
    }

    public async Task<GitHubResponse<T>> Fetch<T>(GitHubClient client, GitHubRequest request) {
      var response = await _next.Fetch<T>(client, request);
      await CheckResponse(response);
      return response;
    }

    public async Task<GitHubResponse<IEnumerable<T>>> FetchPaged<T, TKey>(GitHubClient client, GitHubRequest request, Func<T, TKey> keySelector) {
      var response = await _next.FetchPaged(client, request, keySelector);
      await CheckResponse(response);
      return response;
    }

    private async Task CheckResponse(GitHubResponse response) {
      // Only check for Unauthorized, Forbidden can be other things.
      // Maybe actually check the token validity explicitly first.
      if (response.Status == HttpStatusCode.Unauthorized) {
        await _revoked(response.CacheData.AccessToken);
      }
    }
  }
}
