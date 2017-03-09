namespace RealArtists.ShipHub.Common.DataModel.Types {
  using System;
  using System.Collections.Generic;
  using System.Linq;

  public interface IChangeSummary {
    IEnumerable<long> Organizations { get; }
    IEnumerable<long> Repositories { get; }
    IEnumerable<long> Users { get; }
    bool IsEmpty { get; }
  }

  public class ChangeSummary : IChangeSummary {
    public static IChangeSummary Empty { get; } = new EmptyChangeSummary();
    private class EmptyChangeSummary : IChangeSummary {
      public IEnumerable<long> Organizations { get; } = Array.Empty<long>();
      public IEnumerable<long> Repositories { get; } = Array.Empty<long>();
      public IEnumerable<long> Users { get; } = Array.Empty<long>();
      public bool IsEmpty { get; } = true;
    }

    public ISet<long> Organizations { get; private set; } = new HashSet<long>();
    public ISet<long> Repositories { get; private set; } = new HashSet<long>();
    public ISet<long> Users { get; private set; } = new HashSet<long>();

    public bool IsEmpty => Organizations.Count == 0 && Repositories.Count == 0 && Users.Count == 0;

    IEnumerable<long> IChangeSummary.Organizations => Organizations;
    IEnumerable<long> IChangeSummary.Repositories => Repositories;
    IEnumerable<long> IChangeSummary.Users => Users;

    public ChangeSummary() { }
    public ChangeSummary(IChangeSummary initialValue) {
      UnionWith(initialValue);
    }

    public void Add(long? organizationId = null, long? repositoryId = null, long? userId = null) {
      if (organizationId != null) {
        Organizations.Add(organizationId.Value);
      }

      if (repositoryId != null) {
        Repositories.Add(repositoryId.Value);
      }

      if (userId != null) {
        Users.Add(userId.Value);
      }
    }

    public void UnionWith(IChangeSummary other) {
      if (other != null) {
        Organizations.UnionWith(other.Organizations);
        Repositories.UnionWith(other.Repositories);
        Users.UnionWith(other.Users);
      }
    }

    public static ChangeSummary UnionAll(IEnumerable<IChangeSummary> changes) {
      return new ChangeSummary() {
        Organizations = changes.SelectMany(x => x.Organizations).ToHashSet(),
        Repositories = changes.SelectMany(x => x.Repositories).ToHashSet(),
        Users = changes.SelectMany(x => x.Users).ToHashSet(),
      };
    }

    public override string ToString() {
      return $"ChangeSummary {{ Organizations: [{string.Join(", ", Organizations)}] Repositories: [{string.Join(", ", Repositories)}] Users: [{string.Join(", ", Users)}] }}";
    }
  }
}
