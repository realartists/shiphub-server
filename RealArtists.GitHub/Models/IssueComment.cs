namespace RealArtists.GitHub.Models {
  using System;

  public class IssueComment : GitHubModel {
    public string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Id { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Account User { get; set; }
  }
}
