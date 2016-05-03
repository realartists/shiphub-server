namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  public class GitHubCacheData {
    public string AccessToken { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? Expires { get; set; }
  }
}
