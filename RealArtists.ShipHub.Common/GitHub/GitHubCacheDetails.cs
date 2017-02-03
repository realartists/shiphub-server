namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  [Serializable]
  public class GitHubCacheDetails {
    public virtual long UserId { get; set; }
    public virtual string AccessToken { get; set; }
    public virtual string ETag { get; set; }
    public virtual DateTimeOffset? LastModified { get; set; }
    public virtual DateTimeOffset? Expires { get; set; }
    public virtual TimeSpan PollInterval { get; set; }
    public virtual string Path { get; set; }
  }
}
