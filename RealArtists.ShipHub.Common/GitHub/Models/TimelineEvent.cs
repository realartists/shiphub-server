namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System;
  using System.Collections.Generic;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  //public class TimelineRename {
  //  public string From { get; set; }
  //  public string To { get; set; }
  //}

  //public class ReferenceSource {
  //  public long Id { get; set; }
  //  public Account Actor { get; set; }
  //  public string Url { get; set; }
  //}

  public class TimelineEvent {
    public long Id { get; set; }
    public Account Actor { get; set; }
    public string CommitId { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    //public Label Label { get; set; }
    public Account Assignee { get; set; }
    //public Milestone Milestone { get; set; } // What GitHub sends is incomplete and nearly useless. No need to parse.
    //public ReferenceSource Source { get; set; }
    //public TimelineRename Rename { get; set; }

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
