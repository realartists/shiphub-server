namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using GitHub;

  public class GitHubMetadata : IGitHubCacheMetadata {
    public string AccessToken { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? LastRefresh { get; set; }
    public TimeSpan PollInterval { get; set; }

    // Helper
    public static GitHubMetadata FromResponse(GitHubResponse response) {
      var cacheData = response.CacheData;
      return new GitHubMetadata() {
        AccessToken = cacheData.AccessToken,
        ETag = cacheData.ETag,
        Expires = cacheData.Expires,
        LastModified = cacheData.LastModified,
        LastRefresh = response.Date,
        PollInterval = cacheData.PollInterval,
      };
    }
  }
}
