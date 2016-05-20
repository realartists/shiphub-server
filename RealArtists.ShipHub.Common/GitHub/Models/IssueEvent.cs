namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  //public enum IssueEventType {
  //  Closed,
  //  Reopened,
  //  Subscribed,
  //  Merged,
  //  Referenced,
  //  Mentioned,
  //  Assigned,
  //  Unassigned,
  //  Labeled,
  //  Unlabeled,
  //  Milestoned,
  //  Demilestoned,
  //  Renamed,
  //  Locked,
  //  Unlocked,
  //  [EnumMember(Value = "head_ref_deleted")]
  //  HeadRefDeleted,
  //  [EnumMember(Value = "head_ref_restored")]
  //  HeadRefRestored
  //}

  //public class IssueRename {
  //  public string From { get; set; }
  //  public string To { get; set; }
  //}

  public class IssueEvent {
    public int Id { get; set; }

    [JsonIgnore]
    public DateTimeOffset CreatedAt { get { return _extensionData["created_at"].ToObject<DateTimeOffset>(JsonUtility.SaneSerializer); } }

    //public Account Actor { get; set; }
    //public Account Assignee { get; set; }
    //public Account Assigner { get; set; }
    //public string CommitId { get; set; }
    //public IssueEventType Event { get; set; }
    //public Label Label { get; set; }
    //public Milestone Milestone { get; set; }
    //public IssueRename Rename { get; set; }

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
