namespace RealArtists.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Runtime.Serialization;
  using System.Text;
  using System.Threading.Tasks;

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
    public string CommitId { get; set; }
    public Account Actor { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public IssueLabel Label { get; set; }
    public Account Assignee { get; set; }
    public Account Assigner { get; set; }
    public IssueMilestone Milestone { get; set; }
    public int MyProperty { get; set; }
    
  }
}
