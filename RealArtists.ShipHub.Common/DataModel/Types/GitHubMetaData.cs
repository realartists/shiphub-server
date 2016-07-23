namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using GitHub;
  using Newtonsoft.Json;

  public class GitHubMetaData : IGitHubCacheOptions, IGitHubRequestOptions {
    public string AccessToken { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? LastRefresh { get; set; }

    // Implementing IGitHubRequestOptions is a lazy hack for now.
    [JsonIgnore]
    public IGitHubCredentials Credentials { get { return null; /* TODO: Actually support this */ } }

    [JsonIgnore]
    public IGitHubCacheOptions CacheOptions { get { return this; } }

    // Helper
    public static GitHubMetaData FromResponse(GitHubResponse response) {
      var cacheData = response.CacheData;
      return new GitHubMetaData() {
        AccessToken = cacheData.AccessToken,
        ETag = cacheData.ETag,
        Expires = cacheData.Expires,
        LastModified = cacheData.LastModified,
        LastRefresh = response.Date,
      };
    }
  }
}
