namespace RealArtists.ShipHub.Common.GitHub {
  using System.Collections.Generic;
  using System.Net;
  using Newtonsoft.Json;
  using Newtonsoft.Json.Linq;

  public class GitHubError {
    public HttpStatusCode Status { get; set; }
    public bool IsAbuse => Message?.Contains("abuse") == true;

    public string Message { get; set; }
    public string DocumentationUrl { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionDataDictionary { get; private set; } = new Dictionary<string, JToken>();

    public GitHubException ToException() {
      return new GitHubException(this);
    }

    public override string ToString() {
      return this.SerializeObject(Formatting.None);
    }
  }
}
