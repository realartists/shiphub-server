namespace RealArtists.GitHub.Tests {
  using System;
  using System.Net;
  using System.Net.Http;
  using System.Threading.Tasks;
  using Newtonsoft.Json.Linq;
  using Xunit;

  public class CanEven : IDisposable {
    public GitHubClient _client;

    public CanEven() {
      _client = new GitHubClient("RealArtists.ShipHub.Tests", "0.1");
    }

    public void Dispose() {
      _client.Dispose();
    }

    [Theory]
    [InlineData("kogir")]
    [InlineData("james-howard")]
    public async Task GetAUser(string login) {
      var request = new GitHubRequest(HttpMethod.Get, $"/users/{login}");
      var response = await _client.MakeRequest<JToken>(request);

      Assert.Equal(response.Status, HttpStatusCode.OK);
      Assert.NotNull(response.Result);
      Assert.Equal(response.Result["login"].ToString(), login);

      Assert.Null(response.Error);
      Assert.Null(response.Redirect);
      Assert.Null(response.Pagination);

      Assert.NotNull(response.ETag);
      Assert.NotNull(response.LastModified);

      Assert.True(response.RateLimit > 0);
      Assert.True(response.RateLimitRemaining > 0);
      Assert.True(response.RateLimitReset.Subtract(DateTimeOffset.UtcNow).TotalSeconds > 0);
    }

    public async Task EtagNotModified() {
      var request = new GitHubRequest(HttpMethod.Get, $"/users/kogir");
      var response = await _client.MakeRequest<JToken>(request);

      Assert.Equal(response.Status, HttpStatusCode.OK);
      Assert.NotNull(response.Result);

      request.ETag = response.ETag;
      response = await _client.MakeRequest<JToken>(request);

      Assert.Equal(response.Status, HttpStatusCode.NotModified);
      Assert.Null(response.Result);
    }

    public async Task LastModifiedSince() {
      var request = new GitHubRequest(HttpMethod.Get, $"/users/kogir");
      var response = await _client.MakeRequest<JToken>(request);

      Assert.Equal(response.Status, HttpStatusCode.OK);
      Assert.NotNull(response.Result);

      request.LastModified = response.LastModified;
      response = await _client.MakeRequest<JToken>(request);

      Assert.Equal(response.Status, HttpStatusCode.NotModified);
      Assert.Null(response.Result);
    }
  }
}
