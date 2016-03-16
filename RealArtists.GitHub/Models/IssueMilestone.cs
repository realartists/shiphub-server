namespace RealArtists.GitHub.Models {
  using System;

  public class IssueMilestone : GitHubModel {
    public int Id { get; set; }
    public int Number { get; set; }
    public IssueState State { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public int OpenIssues { get; set; }
    public int ClosedIssues { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? DueOn { get; set; }

    public Account Creator { get; set; }
  }
}
