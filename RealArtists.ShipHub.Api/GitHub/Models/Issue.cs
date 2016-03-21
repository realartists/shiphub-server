namespace RealArtists.ShipHub.Api.GitHub.Models {
  using System;
  using System.Collections.Generic;

  public enum IssueState {
    Open,
    Closed,
  }

  public class Issue : GitHubModel {
    public int Id { get; set; }
    public int Number { get; set; }
    public IssueState State { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public IEnumerable<IssueLabel> Labels { get; set; }
    public Account Assignee { get; set; }
    public Account User { get; set; }
    public IssueMilestone Milestone { get; set; }
    public bool Locked { get; set; }
    public int Comments { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Account ClosedBy { get; set; }
  }
}
