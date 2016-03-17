namespace RealArtists.GitHub {
  using System.Collections.Generic;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public abstract class GitHubModel {
    public JToken this[string key] {
      get {
        return _extensionJson[key];
      }
    }

    [JsonExtensionData]
    public IDictionary<string, JToken> _extensionJson = new Dictionary<string, JToken>();

    [JsonIgnore]
    public string ExtensionJson {
      get {
        return JsonConvert.SerializeObject(_extensionJson, Formatting.Indented, GitHubClient.JsonSettings);
      }
      set {
        if (value != null) {
          _extensionJson = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(value, GitHubClient.JsonSettings);
        }
      }
    }
  }
}
