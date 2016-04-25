namespace RealArtists.ShipHub.Common.GitHub {
  public interface IGitHubRequestOptions {
    IGitHubCredentials Credentials { get; }
    IGitHubCacheOptions CacheOptions { get; }
  }
}