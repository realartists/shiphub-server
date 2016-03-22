namespace RealArtists.ShipHub.Api.GitHub {
  using System;

  public interface IGitHubCacheOptions {
    string ETag { get; }
    DateTimeOffset? LastModified { get; }
  }
}