namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Collections.Generic;

  public class Issue {
    public long Id { get; set; }
    public int Number { get; set; }
    public string State { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public IEnumerable<Label> Labels { get; set; }
    public Account Assignee { get; set; }
    public Account User { get; set; }
    public Milestone Milestone { get; set; }
    public bool Locked { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Account ClosedBy { get; set; }

    public Reactions Reactions { get; set; }
    public PullRequest PullRequest { get; set; }
    public IEnumerable<Account> Assignees { get; set; }
  }
}
