namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System.Collections.Generic;

  public interface IChangeSummary {
    IEnumerable<long> Organizations { get; }
    IEnumerable<long> Repositories { get; }
  }

  public class ChangeSummary : IChangeSummary {
    private HashSet<long> _orgs = new HashSet<long>();
    private HashSet<long> _repos = new HashSet<long>();

    public IEnumerable<long> Organizations { get { return _orgs; } }
    public IEnumerable<long> Repositories { get { return _repos; } }
    public bool Empty { get { return _orgs.Count == 0 && _repos.Count == 0; } }

    public void Add(long? organizationId, long? repositoryId) {
      if (organizationId != null) {
        _orgs.Add(organizationId.Value);
      }

      if (repositoryId != null) {
        _repos.Add(repositoryId.Value);
      }
    }

    public void UnionWith(IChangeSummary other) {
      _orgs.UnionWith(other.Organizations);
      _repos.UnionWith(other.Repositories);
    }
  }
}
