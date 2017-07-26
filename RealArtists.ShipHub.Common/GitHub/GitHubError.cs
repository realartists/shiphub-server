namespace RealArtists.ShipHub.Common.GitHub {
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using System.Net;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public class GitHubError {
    [JsonIgnore]
    public HttpStatusCode Status { get; set; }
    public bool IsAbuse => Message?.Contains("abuse") == true;

    public string Message { get; set; }
    public string DocumentationUrl { get; set; }

    [JsonExtensionData]
    [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
    public IDictionary<string, JToken> ExtensionDataDictionary { get; private set; } = new Dictionary<string, JToken>();

    public GitHubException ToException() {
      return new GitHubException(this);
    }

    public override string ToString() {
      return this.SerializeObject(Formatting.None);
    }
  }
}
