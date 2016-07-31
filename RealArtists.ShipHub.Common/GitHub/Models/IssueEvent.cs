namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public class IssueEvent {
    public long Id { get; set; }
    public Account Actor { get; set; }
    public string Event { get; set; }
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
}
