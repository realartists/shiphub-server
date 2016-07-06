namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System.Collections.Generic;

  public class ChangeSummary {
    private HashSet<long> _orgs = new HashSet<long>();
    private HashSet<long> _repos = new HashSet<long>();

    public IEnumerable<long> Organizations { get { return _orgs; } }
    public IEnumerable<long> Repositories { get { return _repos; } }

    public void Add(long? organizationId, long? repositoryId) {
      if (organizationId != null) {
        _orgs.Add(organizationId.Value);
      }

      if (repositoryId != null) {
        _repos.Add(repositoryId.Value);
      }
    }
  }
}
