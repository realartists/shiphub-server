namespace RealArtists.ShipHub.Common.GitHub.Models {
  using System.Collections.Generic;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public class WebhookConfiguration {
    public string Url { get; set; }
    public string ContentType { get; set; }
    public string Secret { get; set; }

    [JsonIgnore]
    public bool InsecureSsl { get; set; }

    [JsonProperty("insecure_ssl")]
    public JToken GitHubInsecureSsl {
      get {
        // Unsure what GitHub wants. Follow the docs I guess?
        return InsecureSsl ? 1 : 0;
      }
      set {
        switch (value.Type) {
          case JTokenType.Boolean:
            InsecureSsl = value.ToObject<bool>();
            break;
          case JTokenType.Integer:
            InsecureSsl = value.ToObject<int>() == 1;
            break;
          case JTokenType.String:
            InsecureSsl = value.ToObject<string>() == "1";
            break;
          default:
            // Best guess
            InsecureSsl = false;
            break;
        }
      }
    }
  }

  public class Webhook {
    public long Id { get; set; }
    public string Name { get; set; }
    public WebhookConfiguration Config { get; set; }
    public IEnumerable<string> Events { get; set; }
    public bool Active { get; set; }
  }
}
