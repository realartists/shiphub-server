namespace RealArtists.ShipHub.Api {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Threading.Tasks;
  using System.Web;
  using Actors.GitHub;
  using Common;
  using Common.GitHub;
  using Common.GitHub.Models;

  /// <summary>
  /// This is a temporary hack until I can move GitHubActor from UserId based identity to AccessToken.
  /// </summary>
  public class LoginGitHubClient : IGitHubClient {
    public static readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
    public static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    public string AccessToken { get; }

    public Uri ApiRoot { get; } = ShipHubCloudConfiguration.Instance.GitHubApiRoot;
    public Guid CorrelationId { get; } = Guid.NewGuid();
    public GitHubRateLimit RateLimit { get; private set; }
    public ProductInfoHeaderValue UserAgent { get; } = new ProductInfoHeaderValue(ApplicationName, ApplicationVersion);
    public long UserId { get; } = -1;
    public string UserInfo { get; } = "ShipHub Authentication Controller (-1)";

    private IGitHubHandler _handler;

    public LoginGitHubClient(string accessToken, IGitHubHandler handler) {
      AccessToken = accessToken;
      _handler = handler;
    }

    public int NextRequestId() {
      return 1;
    }

    public void UpdateInternalRateLimit(GitHubRateLimit rateLimit) {
      RateLimit = rateLimit;
    }

    public Task<GitHubResponse<Account>> User(GitHubCacheDetails cacheOptions = null) {
      var request = new GitHubRequest("user", cacheOptions);
      return _handler.Fetch<Account>(this, request);
    }
  }
}