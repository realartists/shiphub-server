namespace RealArtists.ShipHub.Api.GitHub {
  public interface IGitHubRequestOptions {
    IGitHubCredentials Credentials { get; }
    IGitHubCacheOptions CacheOptions { get; }
  }
}