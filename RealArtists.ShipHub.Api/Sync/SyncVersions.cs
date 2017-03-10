namespace RealArtists.ShipHub.Api.Sync {
  using System.Collections.Generic;

  public class SyncVersions {
    public SyncVersions() {
      RepoVersions = new Dictionary<long, long>();
      OrgVersions = new Dictionary<long, long>();
    }

    public SyncVersions(IDictionary<long, long> repoVersions, IDictionary<long, long> orgVersions, long? pullRequestVersion) {
      RepoVersions = repoVersions ?? new Dictionary<long, long>();
      OrgVersions = orgVersions ?? new Dictionary<long, long>();
      PullRequestVersion = pullRequestVersion ?? 0;
    }

    public IDictionary<long, long> RepoVersions { get; }
    public IDictionary<long, long> OrgVersions { get; }
    public long PullRequestVersion { get; set; } = 0;
  }
}
