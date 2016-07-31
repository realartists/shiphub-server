namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public class TimelineEvent {
    public long Id { get; set; }
    public Account Actor { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Account Assignee { get; set; }
    //public Milestone Milestone { get; set; } // What GitHub sends is incomplete and nearly useless, often just the name. No need to parse.

    // We want these to be saved in _extensionData, so don't actually deserialize them.
    [JsonIgnore]
    public string CommitId { get { return ExtensionDataDictionary.Val("commit_id")?.ToObject<string>(); } }

    [JsonIgnore]
    public string CommitUrl { get { return ExtensionDataDictionary.Val("commit_url")?.ToObject<string>(); } }

    [JsonIgnore]
    public Label Label { get { return ExtensionDataDictionary.Val("label")?.ToObject<Label>(); } }

    [JsonIgnore]
    public ReferenceSource Source { get { return ExtensionDataDictionary.Val("source")?.ToObject<ReferenceSource>(); } }

    [JsonIgnore]
    public TimelineRename Rename { get { return ExtensionDataDictionary.Val("rename")?.ToObject<TimelineRename>(); } }

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

  public class TimelineRename {
    public string From { get; set; }
    public string To { get; set; }
  }

  public class ReferenceSource {
    public Account Actor { get; set; }

    [JsonProperty("id")]
    public long CommentId { get; set; }

    [JsonProperty("url")]
    public string IssueUrl { get; set; }
  }
}
