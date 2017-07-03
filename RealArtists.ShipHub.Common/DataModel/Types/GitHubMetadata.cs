namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using GitHub;

  public class GitHubMetadata {
    public long UserId { get; set; }
    public string AccessToken { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? LastRefresh { get; set; }
    public TimeSpan PollInterval { get; set; }
    public string Path { get; set; }

    public static implicit operator GitHubCacheDetails(GitHubMetadata metadata) {
      if (metadata == null) {
        return null;
      }

      return new GitHubCacheDetails() {
        UserId = metadata.UserId,
        AccessToken = metadata.AccessToken,
        ETag = metadata.ETag,
        Expires = metadata.Expires,
        LastModified = metadata.LastModified,
        PollInterval = metadata.PollInterval,
        Path = metadata.Path,
      };
    }

    public static GitHubMetadata FromResponse(GitHubResponse response) {
      if (response?.Succeeded != true) {
        return null;
      }

      var cacheData = response.CacheData;
      return new GitHubMetadata() {
        UserId = cacheData.UserId,
        AccessToken = cacheData.AccessToken,
        ETag = cacheData.ETag,
        Expires = cacheData.Expires,
        LastModified = cacheData.LastModified,
        LastRefresh = response.Date,
        PollInterval = cacheData.PollInterval,
        Path = cacheData.Path,
      };
    }
  }

  public static class GitHubMetadataExtensions {
    /// <summary>
    /// Tests if metadata indicates a resource is expired. Returns true for:
    /// null metadata
    /// metadata with null Expires
    /// metadata with Expires < NOW
    /// </summary>
    public static bool IsExpired(this GitHubMetadata metadata) {
      return metadata?.Expires == null || metadata.Expires < DateTimeOffset.UtcNow;
    }
  }
}
