namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  public interface IGitHubCacheOptions {
    string ETag { get; }
    DateTimeOffset? LastModified { get; }
  }
}