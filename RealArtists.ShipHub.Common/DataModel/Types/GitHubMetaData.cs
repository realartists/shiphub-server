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
      };
    }

    public static GitHubMetadata FromResponse(GitHubResponse response) {
      // Should we drop in all error cases? I think yes.
      if (response == null || response.IsError || response.CacheData == null) {
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
      };
    }
  }

  public static class GitHubMetadataExtensions {
    public static GitHubMetadata IfValidFor(this GitHubMetadata metadata, Account account) {
      if (metadata?.AccessToken == account?.Token) {
        return metadata;
      }

      return null;
    }
  }
}
