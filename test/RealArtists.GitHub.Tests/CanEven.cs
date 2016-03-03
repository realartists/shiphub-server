namespace RealArtists.GitHub.Tests {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Threading.Tasks;
  using Newtonsoft.Json.Linq;
  using Xunit;

  public class CanEven : IDisposable {
    public GitHubClient _client;

    public CanEven() {
      _client = new GitHubClient();
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
      Assert.Null(response.Error);
      Assert.NotNull(response.RateLimit);
      Assert.Null(response.Redirect);
      Assert.NotNull(response.ETag);
      Assert.NotNull(response.LastModified);
      Assert.NotNull(response.Result);
      Assert.Equal(response.Result["login"].ToString(), login);
    }
  }
}
