namespace RealArtists.ShipHub.Common.GitHub {
  using System;

  public interface IGitHubCacheDetails {
    string AccessToken { get; }
    string ETag { get; }
    DateTimeOffset? Expires { get; }
    DateTimeOffset? LastModified { get; }
    TimeSpan PollInterval { get; }
  }

  public class GitHubCacheDetails : IGitHubCacheDetails {
    public static IGitHubCacheDetails Empty { get; } = new EmptyGitHubCacheDetails();

    public string AccessToken { get; set; }
    public string ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? Expires { get; set; }
    public TimeSpan PollInterval { get; set; }

    // Hidden from public view...
    private class EmptyGitHubCacheDetails : IGitHubCacheDetails {
      public string AccessToken { get { return null; } }
      public string ETag { get { return null; } }
      public DateTimeOffset? Expires { get { return null; } }
      public DateTimeOffset? LastModified { get { return null; } }
      public TimeSpan PollInterval { get { return TimeSpan.Zero; } }
    }
  }
}
