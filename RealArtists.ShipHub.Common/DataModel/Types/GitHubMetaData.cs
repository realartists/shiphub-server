﻿namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using GitHub;

  public class GitHubMetadata {
    public string AccessToken { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? LastRefresh { get; set; }
    public TimeSpan PollInterval { get; set; }

    public IGitHubCacheDetails AsCacheDetails() {
      return new GitHubCacheDetails() {
        AccessToken = AccessToken,
        ETag = ETag,
        Expires = Expires,
        LastModified = LastModified,
        PollInterval = PollInterval,
      };
    }

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

  public static class GitHubMetadataExtensions {
    public static GitHubMetadata IfValidFor(this GitHubMetadata metadata, Account account) {
      if (metadata?.AccessToken == account?.Token) {
        return metadata;
      }

      return null;
    }
  }
}
