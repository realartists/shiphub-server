namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  //public enum IssueEventType {
  //  [EnumMember(Value = "closed")]
  //  Closed,
  //  [EnumMember(Value = "reopened")]
  //  Reopened,
  //  [EnumMember(Value = "subscribed")]
  //  Subscribed,
  //  [EnumMember(Value = "merged")]
  //  Merged,
  //  [EnumMember(Value = "referenced")]
  //  Referenced,
  //  [EnumMember(Value = "mentioned")]
  //  Mentioned,
  //  [EnumMember(Value = "assigned")]
  //  Assigned,
  //  [EnumMember(Value = "unassigned")]
  //  Unassigned,
  //  [EnumMember(Value = "labeled")]
  //  Labeled,
  //  [EnumMember(Value = "unlabeled")]
  //  Unlabeled,
  //  [EnumMember(Value = "milestoned")]
  //  Milestoned,
  //  [EnumMember(Value = "demilestoned")]
  //  Demilestoned,
  //  [EnumMember(Value = "renamed")]
  //  Renamed,
  //  [EnumMember(Value = "locked")]
  //  Locked,
  //  [EnumMember(Value = "unlocked")]
  //  Unlocked,
  //  [EnumMember(Value = "head_ref_deleted")]
  //  HeadRefDeleted,
  //  [EnumMember(Value = "head_ref_restored")]
  //  HeadRefRestored
  //}

  public class IssueRename {
    public string From { get; set; }
    public string To { get; set; }
  }

  public class IssueEvent {
    public long Id { get; set; }
    public Account Actor { get; set; }
    public string CommitId { get; set; }
    public string CommitUrl { get; set; }
    //public IssueEventType Event { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    //public Label Label { get; set; }
    public Account Assignee { get; set; }
    public Account Assigner { get; set; }
    //public Milestone Milestone { get; set; } // What GitHub sends is incomplete and nearly useless. No need to parse.
    //public IssueRename Rename { get; set; }
    public Issue Issue { get; set; }  // Only present when requesting repository events.

    /// <summary>
    /// Just in case (for future compatibility)
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken> _extensionData = new Dictionary<string, JToken>();

    [JsonIgnore]
    public string ExtensionData {
      get {
        return _extensionData.SerializeObject(Formatting.None);
      }
      set {
        if (value != null) {
          _extensionData = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(value);
        } else {
          _extensionData.Clear();
        }
      }
    }
  }
}
