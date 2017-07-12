namespace RealArtists.ShipHub.Api.Tests {
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Common.DataModel;
  using Common.DataModel.Types;

  public static class SuperLazyRefactoringHelpers {
    /// <summary>
    /// This is gross and I feel ashamed, but it's easier than refactoring.
    /// </summary>
    public static Task<ChangeSummary> SetAccountLinkedRepositories(this ShipHubContext context, long userId, IEnumerable<(long RepositoryId, bool Admin)> repos) {
      var permissions = repos.Select(x => new RepositoryPermissionsTableType() {
        RepositoryId = x.RepositoryId,
        Admin = x.Admin,
        Pull = true,
        Push = true,
      });

      return context.SetAccountLinkedRepositories(userId, permissions);
    }
  }
}
