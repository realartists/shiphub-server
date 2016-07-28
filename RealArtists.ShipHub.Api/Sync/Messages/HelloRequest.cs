namespace RealArtists.ShipHub.Api.Sync.Messages {
  using System.Collections.Generic;
  using Newtonsoft.Json;

  public class RepositoryVersion {
    [JsonProperty("repo")]
    public long Id { get; set; }
    public long Version { get; set; }
  }

  public class OrganizationVersion {
    [JsonProperty("org")]
    public long Id { get; set; }
    public long Version { get; set; }
  }

  public class VersionDetails {
    [JsonProperty("repos")]
    public IEnumerable<RepositoryVersion> Repositories { get; set; }

    [JsonProperty("orgs")]
    public IEnumerable<OrganizationVersion> Organizations { get; set; }
  }

  public class HelloRequest : SyncMessageBase {
    public string Client { get; set; }
    public VersionDetails Versions { get; set; }
  }
}
