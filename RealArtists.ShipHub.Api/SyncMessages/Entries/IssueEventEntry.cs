namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System;
  using System.Collections.Generic;
  using Common;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public class IssueEventEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Repository { get; set; }
    public long Issue { get; set; }
    public long Actor { get; set; }
    public string CommitId { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long? Assignee { get; set; }

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
