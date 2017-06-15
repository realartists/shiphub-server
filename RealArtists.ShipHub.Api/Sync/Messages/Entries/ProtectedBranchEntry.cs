using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RealArtists.ShipHub.Common;

namespace RealArtists.ShipHub.Api.Sync.Messages.Entries {
  public class ProtectedBranchEntry : SyncEntity {
    public long Identifier { get; set; }

    public string Name { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public long? Repository { get; set; }

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