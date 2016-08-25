namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  public interface IGitHubCacheMetadata {
    string AccessToken { get; }
    string ETag { get; }
    DateTimeOffset? Expires { get; }
    DateTimeOffset? LastModified { get; }
    TimeSpan PollInterval { get; }
  }

  public class GitHubCacheMetadata : IGitHubCacheMetadata {
    public string AccessToken { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public TimeSpan PollInterval { get; set; }
  }
}
