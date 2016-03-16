namespace RealArtists.GitHub {
  using System.Collections.Generic;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public abstract class GitHubModel {
    public JToken this[string key] {
      get {
        return _extensionData[key];
      }
    }

    [JsonExtensionData]
    public IDictionary<string, JToken> _extensionData = new Dictionary<string, JToken>();

    [JsonIgnore]
    public string ExtensionData {
      get {
        return JsonConvert.SerializeObject(_extensionData, Formatting.Indented, GitHubClient.JsonSettings);
      }
      set {
        if (value != null) {
          _extensionData = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(value, GitHubClient.JsonSettings);
        }
      }
    }
  }
}
