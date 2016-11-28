namespace RealArtists.ShipHub.Api.Sync.Messages {
  using System.Collections.Generic;
  using System.Linq;
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

    // Helpers to parse client user agent string
    // Example Client: 
    // com.realartists.Ship2 2.0 (1), OS X 10.11.6

    /// <summary>
    /// The product identifier of the client. Ex: com.realartists.Ship2
    /// </summary>
    public string ClientIdentifier {
      get {
        return Client.Split(' ').FirstOrDefault();
      }
    }

    /// <summary>
    /// The marketing version of the client. Ex: 2.0
    /// </summary>
    public string ClientVersion {      
      get {
        return Client.Split(' ').Skip(1).FirstOrDefault();
      }
    }

    /// <summary>
    /// The build number of the client. Ex: 337. Returns int.MaxValue for development builds, which the client reports as (1).
    /// </summary>
    public int ClientBuild {
      get {
        var x = Client.Split(' ').Skip(2).FirstOrDefault();
        if (x == null || x.Length < 4) {
          return 0;
        }
        x = x.Substring(1, x.Length - 3); // strip (),
        int b;
        if (!int.TryParse(x, out b)) {
          b = 0;
        }
        return b <= 1 ? int.MaxValue : b;
      }
    }

    /// <summary>
    /// The OS name and version of the client. Ex: OS X 10.11.6
    /// </summary>
    public string ClientOS {
      get {
        var os = (Client.Split(',').Skip(1).FirstOrDefault() ?? "").Trim();
        return os;
      }
    }

    /// <summary>
    /// The OS name of the client. Ex: OS X
    /// </summary>
    public string ClientOSName {
      get {
        var os = ClientOS;
        var parts = os.Split(' ');
        var name = string.Join(" ", parts.Take(parts.Length - 1).ToArray());
        return name;
      }
    }

    /// <summary>
    /// The OS version of the client. Ex: 10.11.6
    /// </summary>
    public string ClientOSVersion {
      get {
        var os = ClientOS;
        var parts = os.Split(' ');
        var version = parts[parts.Length - 1];
        return version;
      }
    }
  }
}
