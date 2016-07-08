namespace RealArtists.ShipHub.Api.Sync {
  using System.Collections.Generic;

  public class SyncVersions {
    public Dictionary<long, long> RepositoryVersions { get; set; } = new Dictionary<long, long>();
    public Dictionary<long, long> OrgVersions { get; set; } = new Dictionary<long, long>();
  }
}
