namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  [Serializable]
  public class GitHubCacheDetails {
    public static GitHubCacheDetails Empty { get; } = new EmptyGitHubCacheDetails();

    public virtual long UserId { get; set; }
    public virtual string AccessToken { get; set; }
    public virtual string ETag { get; set; }
    public virtual DateTimeOffset? LastModified { get; set; }
    public virtual DateTimeOffset? Expires { get; set; }
    public virtual TimeSpan PollInterval { get; set; }

    // Hidden from public view...
    [Serializable]
    private class EmptyGitHubCacheDetails : GitHubCacheDetails {
      public override long UserId { get { return 0; } }
      public override string AccessToken { get { return null; } }
      public override string ETag { get { return null; } }
      public override DateTimeOffset? Expires { get { return null; } }
      public override DateTimeOffset? LastModified { get { return null; } }
      public override TimeSpan PollInterval { get { return TimeSpan.Zero; } }
    }
  }
}
