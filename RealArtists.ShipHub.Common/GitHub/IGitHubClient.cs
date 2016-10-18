namespace RealArtists.ShipHub.Common.GitHub {
  using System;
  using System.Net.Http.Headers;

  public interface IGitHubClient {
    Uri ApiRoot { get; }
    Guid CorrelationId { get; }
    string DefaultToken { get; set; }
    IGitHubHandler Handler { get; set; }
    GitHubRateLimit RateLimit { get; }
    ProductInfoHeaderValue UserAgent { get; }
    string UserInfo { get; }

    int NextRequestId();
    void UpdateInternalRateLimit(GitHubRateLimit rateLimit);
  }
}
