namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  using System;
  using System.Collections.Generic;
  using Common;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public class IssueEventEntry : SyncEntity {
    public long Identifier { get; set; }
    public long Repository { get; set; }
    public long Issue { get; set; }
    public long? Actor { get; set; }
    public string Event { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

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
  }
}
