namespace RealArtists.ShipHub.Api.Sync {
  using System.Collections.Generic;
  using Newtonsoft.Json.Linq;

  public class SyncVersions {
    public SyncVersions() {
      RepoVersions = new Dictionary<long, long>(); ;
      OrgVersions = new Dictionary<long, long>(); ;
    }

    public SyncVersions(IDictionary<long, long> repoVersions, IDictionary<long, long> orgVersions) {
      RepoVersions = repoVersions ?? new Dictionary<long, long>();
      OrgVersions = orgVersions ?? new Dictionary<long, long>();
    }

    public IDictionary<long, long> RepoVersions { get; }
    public IDictionary<long, long> OrgVersions { get; }

    public override string ToString() {
      return $"SyncVersions RepoVersion: {JObject.FromObject(RepoVersions)} OrgVersions: {JObject.FromObject(OrgVersions)}";
    }
  }
}
