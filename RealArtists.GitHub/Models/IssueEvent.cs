namespace RealArtists.GitHub.Models {
  using System;
  using System.Runtime.Serialization;

  public enum GitHubIssueEvent {
    Closed,
    Reopened,
    Subscribed,
    Merged,
    Referenced,
    Mentioned,
    Assigned,
    Unassigned,
    Labeled,
    Unlabeled,
    Milestoned,
    Demilestoned,
    Renamed,
    Locked,
    Unlocked,
    [EnumMember(Value = "head_ref_deleted")]
    HeadRefDeleted,
    [EnumMember(Value = "head_ref_restored")]
    HeadRefRestored
  }

  public class IssueEvent : GitHubModel {
    public int Id { get; set; }
    public Account Actor { get; set; }
    public Account Assignee { get; set; }
    public Account Assigner { get; set; }
    public string CommitId { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public IssueLabel Label { get; set; }
    public IssueMilestone Milestone { get; set; }
    public IssueRename Rename { get; set; }
  }
}
