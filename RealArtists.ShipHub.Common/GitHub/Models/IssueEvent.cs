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
    public Account Assigner {
      get => Actor;
      set => Actor = value;
    }

    // Only present when requesting repository events.
    public Issue Issue { get; set; }

    // Required for AutoMapper when Issue is null
    [JsonIgnore]
    public long IssueId => Issue?.Id ?? FallbackIssueId;

    ///////////////////////////////////
    // We want these to be saved in _extensionData, so don't actually deserialize them.
    ///////////////////////////////////
    [JsonIgnore]
    public Account Assignee => ExtensionDataDictionary.Val("assignee")?.ToObject<Account>();

    [JsonIgnore]
    public string CommitId => ExtensionDataDictionary.Val("commit_id")?.ToObject<string>();

    [JsonIgnore]
    public string CommitUrl => ExtensionDataDictionary.Val("commit_url")?.ToObject<string>();

    [JsonIgnore]
    public Label Label => ExtensionDataDictionary.Val("label")?.ToObject<Label>();

    [JsonIgnore]
    public IssueRename Rename => ExtensionDataDictionary.Val("rename")?.ToObject<IssueRename>();

    [JsonIgnore]
    public string ShaHash => ExtensionDataDictionary.Val("sha")?.ToObject<string>();

    [JsonIgnore]
    public ReferenceSource Source => ExtensionDataDictionary.Val("source")?.ToObject<ReferenceSource>();

    ///////////////////////////////////
    // Json bag
    ///////////////////////////////////
    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionDataDictionary { get; private set; } = new Dictionary<string, JToken>();

    [JsonIgnore]
    public string ExtensionData {
      get => ExtensionDataDictionary.SerializeObject(Formatting.None);
      set {
        if (value != null) {
          ExtensionDataDictionary = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(value);
        } else {
          ExtensionDataDictionary.Clear();
        }
      }
    }

    private long? _fallbackIssueId;
    [JsonIgnore]
    public long FallbackIssueId {
      get {
        if (!_fallbackIssueId.HasValue) {
          throw new InvalidOperationException("FallbackIssueId has not been set.");
        }
        return _fallbackIssueId.Value;
      }
      set {
        if (Issue != null) {
          throw new InvalidOperationException("Cannot override official Issue Id.");
        }
        _fallbackIssueId = value;
      }
    }

    /// <summary>
    /// Used for deduplicating paginated responses and detecting updates in the DB.
    /// </summary>
    [JsonIgnore]
    public string UniqueKey {
      get {
        if (Id.HasValue) {
          // normal events that have ids
          return $"N{Id}";
        } else if (Event == "committed") {
          // committed events (in PRs)
          return $"C_{IssueId}_{ShaHash}";
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

  /// <summary>
  /// A poorly specified bag of frustration.
  /// </summary>
  public class ReferenceSource {
    /// <summary>
    /// Comment Id, only for cross-referenced comments
    /// </summary>
    public long? Id { get; set; }

    /// <summary>
    /// Comment URL, only for cross-referenced comments
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Comment Author, only for cross-referenced comments
    /// </summary>
    public Account Actor { get; set; }

    /// <summary>
    /// Only for cross-referenced issue bodies
    /// </summary>
    public Issue Issue { get; set; }
  }
}
