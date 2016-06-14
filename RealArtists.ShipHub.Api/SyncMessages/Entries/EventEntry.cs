namespace RealArtists.ShipHub.Api.SyncMessages.Entries {
  using System.Collections.Generic;
  using Common;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public class EventEntry : SyncEntity {
    public long Identifier { get; set; }
    public long RepositoryIdentifier { get; set; }

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
