namespace RealArtists.ShipHub.Api.Sync {
  using System.Collections.Generic;
  using System.Linq;

  public class SyncVersions {
    public SyncVersions() {
      RepoVersions = new Dictionary<long, long>();
      OrgVersions = new Dictionary<long, long>();
    }

    public SyncVersions(IDictionary<long, long> repoVersions, IDictionary<long, long> orgVersions, long? pullRequestVersion, long? mentionsVersion, long? queriesVersion, long? mergeRestrictionVersion) {
      RepoVersions = repoVersions ?? new Dictionary<long, long>();
      OrgVersions = orgVersions ?? new Dictionary<long, long>();
      PullRequestVersion = pullRequestVersion ?? 0;
      MentionsVersion = mentionsVersion ?? 0;
      QueriesVersion = queriesVersion ?? 0;
      MergeRestrictionVersion = mergeRestrictionVersion ?? 0;
    }

    public void ResyncAll() {
      RepoVersions = RepoVersions.ToDictionary(x => x.Key, x => 0L);
      OrgVersions = OrgVersions.ToDictionary(x => x.Key, x => 0L);
    }

    public IDictionary<long, long> RepoVersions { get; private set; }
    public IDictionary<long, long> OrgVersions { get; private set; }
    public long PullRequestVersion { get; set; } = 0;
    public long MentionsVersion { get; set; } = 0;
    public long QueriesVersion { get; set; } = 0;
    public long MergeRestrictionVersion { get; set; } = 0;
  }
}
