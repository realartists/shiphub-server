namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using System.Runtime.Serialization;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public class IssueEvent {
    public long Id { get; set; }
    public Account Actor { get; set; }
    public IssueEventType Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Account Assignee { get; set; }
    public Account Assigner { get; set; } // This replaces Actor for assigned events
    //public Milestone Milestone { get; set; } // What GitHub sends is incomplete and nearly useless, often just the name. No need to parse.
    public Issue Issue { get; set; }  // Only present when requesting repository events.

    // We want these to be saved in _extensionData, so don't actually deserialize them.
    [JsonIgnore]
    public string CommitId { get { return ExtensionDataDictionary.Val("commit_id")?.ToObject<string>(); } }

    [JsonIgnore]
    public string CommitUrl { get { return ExtensionDataDictionary.Val("commit_url")?.ToObject<string>(); } }

    [JsonIgnore]
    public Label Label { get { return ExtensionDataDictionary.Val("label")?.ToObject<Label>(); } }

    [JsonIgnore]
    public IssueRename Rename { get { return ExtensionDataDictionary.Val("rename")?.ToObject<IssueRename>(); } }

    /// <summary>
    /// Just in case (for future compatibility)
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionDataDictionary { get; private set; } = new Dictionary<string, JToken>();

    [JsonIgnore]
    public string ExtensionData {
      get {
        return ExtensionDataDictionary.SerializeObject(Formatting.None);
      }
      set {
        if (value != null) {
          ExtensionDataDictionary = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(value);
        } else {
          ExtensionDataDictionary.Clear();
        }
      }
    }
  }

  public class IssueRename {
    public string From { get; set; }
    public string To { get; set; }
  }

  public enum IssueEventType {
    /// <summary>
    /// Not a valid value. Uninitialized or an error.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The issue was closed by the actor. When the commit_id is present, it identifies the commit that closed the issue using "closes / fixes #NN" syntax.
    /// </summary>
    [EnumMember(Value = "closed")]
    Closed,

    /// <summary>
    /// The issue was reopened by the actor.
    /// </summary>
    [EnumMember(Value = "reopened")]
    Reopened,

    /// <summary>
    /// The actor subscribed to receive notifications for an issue.
    /// </summary>
    [EnumMember(Value = "subscribed")]
    Subscribed,

    /// <summary>
    /// The issue was merged by the actor. The `commit_id` attribute is the SHA1 of the HEAD commit that was merged.
    /// </summary>
    [EnumMember(Value = "merged")]
    Merged,

    /// <summary>
    /// The issue was referenced from a commit message. The `commit_id` attribute is the commit SHA1 of where that happened.
    /// </summary>
    [EnumMember(Value = "referenced")]
    Referenced,

    /// <summary>
    /// The actor was @mentioned in an issue body.
    /// </summary>
    [EnumMember(Value = "mentioned")]
    Mentioned,

    /// <summary>
    /// The issue was assigned to the actor.
    /// </summary>
    [EnumMember(Value = "assigned")]
    Assigned,

    /// <summary>
    /// The actor was unassigned from the issue.
    /// </summary>
    [EnumMember(Value = "unassigned")]
    Unassigned,

    /// <summary>
    /// A label was added to the issue.
    /// </summary>
    [EnumMember(Value = "labeled")]
    Labeled,

    /// <summary>
    /// A label was removed from the issue.
    /// </summary>
    [EnumMember(Value = "unlabeled")]
    Unlabeled,

    /// <summary>
    /// The issue was added to a milestone.
    /// </summary>
    [EnumMember(Value = "milestoned")]
    Milestoned,

    /// <summary>
    /// The issue was removed from a milestone.
    /// </summary>
    [EnumMember(Value = "demilestoned")]
    Demilestoned,

    /// <summary>
    /// The issue title was changed.
    /// </summary>
    [EnumMember(Value = "renamed")]
    Renamed,

    /// <summary>
    /// The issue was locked by the actor.
    /// </summary>
    [EnumMember(Value = "locked")]
    Locked,

    /// <summary>
    /// The issue was unlocked by the actor.
    /// </summary>
    [EnumMember(Value = "unlocked")]
    Unlocked,

    /// <summary>
    /// The pull request's branch was deleted.
    /// </summary>
    [EnumMember(Value = "head_ref_deleted")]
    HeadRefDeleted,

    /// <summary>
    /// The pull request's branch was restored
    /// </summary>
    [EnumMember(Value = "head_ref_restored")]
    HeadRefRestored
  }
}
