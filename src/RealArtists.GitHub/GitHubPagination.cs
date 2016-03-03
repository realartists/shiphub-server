namespace RealArtists.GitHub {
  using System;

  public class GitHubPagination {
    public Uri Next { get; set; }
    public Uri Last { get; set; }
    public Uri First { get; set; }
    public Uri Previous { get; set; }
  }
}
