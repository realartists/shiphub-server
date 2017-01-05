namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  /// <summary>
  /// IssueEvents and TimelineEvents and close enough that I want to share some logic.
  /// </summary>
  public class IssueEvent {
    public long? Id { get; set; }
    public Account Actor { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // What GitHub sends is incomplete and nearly useless, often just the name. No need to parse.
    //public Milestone Milestone { get; set; } 

    // Only for IssueEvents, should replace Actor when set.
    public Account Assigner { get { return Actor; } set { Actor = value; } }

    // Only present when requesting repository events.
    public Issue Issue { get; set; }

    ///////////////////////////////////
    // We want these to be saved in _extensionData, so don't actually deserialize them.
    ///////////////////////////////////
    [JsonIgnore]
    public Account Assignee { get { return ExtensionDataDictionary.Val("assignee")?.ToObject<Account>(); } }

    [JsonIgnore]
    public string CommitId { get { return ExtensionDataDictionary.Val("commit_id")?.ToObject<string>(); } }

    [JsonIgnore]
    public string CommitUrl { get { return ExtensionDataDictionary.Val("commit_url")?.ToObject<string>(); } }

    [JsonIgnore]
    public Label Label { get { return ExtensionDataDictionary.Val("label")?.ToObject<Label>(); } }

    [JsonIgnore]
    public IssueRename Rename { get { return ExtensionDataDictionary.Val("rename")?.ToObject<IssueRename>(); } }

    [JsonIgnore]
    public ReferenceSource Source { get { return ExtensionDataDictionary.Val("source")?.ToObject<ReferenceSource>(); } }

    ///////////////////////////////////
    // Json bag
    ///////////////////////////////////
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

    private long _issueId = 0;
    [JsonIgnore]
    public long IssueId {
      get {
        if (Issue != null)
          return Issue.Id;
        else
          return _issueId;
      }
      set {
        _issueId = value;
      }
    }

    [JsonIgnore]
    public string UniqueKey {
      get {
        if (Id.HasValue) {
          // normal events that have ids
          return $"N{Id}";
        } else if (Source != null) {
          if (!string.IsNullOrEmpty(Source.Url)) {
            // cross-referenced by a comment (this is the comment URL)
            return $"U{IssueId}.{Source.Url}";
          } else if (Source.Issue != null) {
            // cross-referenced by an issue body (this is the issue id)
            return $"I{IssueId}.{Source.Issue.Id}";
          }
        }
        throw new NotSupportedException($"Cannot determine a UniqueKey for this event {ExtensionData}");
      }
    }
  }

  public class IssueRename {
    public string From { get; set; }
    public string To { get; set; }
  }

  public class ReferenceSource {
    public Account Actor { get; set; }

    [JsonProperty("id")]
    public long? Id { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("issue")]
    public Issue Issue { get; set; }
  }
}
