namespace RealArtists.ShipHub.Api.Sync.Messages {
  using System.Collections.Generic;
  using System.Runtime.Serialization;

  public enum SyncLogAction {
    [EnumMember(Value = "oops")]
    Unspecified = 0, // Catch uninitialized values

    [EnumMember(Value = "set")]
    Set,

    [EnumMember(Value = "delete")]
    Delete
  }

  public enum SyncEntityType {
    [EnumMember(Value = "oops")]
    Unspecified = 0, // Catch uninitialized values

    [EnumMember(Value = "comment")]
    IssueComment,

    [EnumMember(Value = "event")]
    Event,

    [EnumMember(Value = "issue")]
    Issue,

    [EnumMember(Value = "milestone")]
    Milestone,

    [EnumMember(Value = "org")]
    Organization,

    [EnumMember(Value = "repo")]
    Repository,

    [EnumMember(Value = "user")]
    User,

    [EnumMember(Value = "reaction")]
    Reaction,

    [EnumMember(Value = "label")]
    Label,

    [EnumMember(Value = "project")]
    Project,

    [EnumMember(Value = "pullrequest")]
    PullRequest,

    [EnumMember(Value = "prcomment")]
    PullRequestComment,

    [EnumMember(Value = "prreview")]
    Review,

    [EnumMember(Value = "commitstatus")]
    CommitStatus,

    [EnumMember(Value = "commitcomment")]
    CommitComment,

    [EnumMember(Value = "protectedbranch")]
    ProtectedBranch
  }

  public abstract class SyncEntity {
    // Empty
  }

  public class SyncLogEntry {
    public SyncLogAction Action { get; set; }
    public SyncEntityType Entity { get; set; }
    public SyncEntity Data { get; set; }
  }

  public class SyncSpiderProgress {
    public string Summary { get; set; }
    public double Progress { get; set; } // < 0 = indeterminate, otherwise in the range 0..1
  }

  public class SyncResponse : SyncMessageBase {
    public override string MessageType { get => "sync"; set { } }

    public IEnumerable<SyncLogEntry> Logs { get; set; }
    public VersionDetails Versions { get; set; }
    public long Remaining { get; set; }
    public SyncSpiderProgress SpiderProgress { get; set; }
  }
}
