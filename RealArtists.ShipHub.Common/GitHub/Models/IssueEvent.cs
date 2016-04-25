namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Runtime.Serialization;
  using Newtonsoft.Json;

  public enum IssueEventType {
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

  public class IssueRename : GitHubModel {
    public string From { get; set; }
    public string To { get; set; }
  }

  public class IssueEvent : GitHubModel {
    public int Id { get; set; }
    public Account Actor { get; set; }
    public Account Assignee { get; set; }
    public Account Assigner { get; set; }
    public string CommitId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Label Label { get; set; }
    public Milestone Milestone { get; set; }
    public IssueRename Rename { get; set; }

    [JsonProperty("event")]
    public IssueEventType EventType { get; set; }
  }
}
