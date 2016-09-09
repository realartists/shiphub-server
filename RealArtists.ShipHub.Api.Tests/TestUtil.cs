namespace RealArtists.ShipHub.Api.Tests {
  using System;
  using System.Linq;
  using Common.DataModel.Types;

  public class TestUtil {
    public static Common.DataModel.User MakeTestUser(Common.DataModel.ShipHubContext context, long userId = 3001, string login = "aroon") {
      context.BulkUpdateAccounts(DateTimeOffset.UtcNow, new[] {
        new AccountTableType() {
          Id = userId,
          Login = login,
          Type = "user",
        },
      }).GetAwaiter().GetResult();
      var user = context.Users.Single(x => x.Id == userId);
      user.Token = Guid.NewGuid().ToString();
      return user;
    }

    public static Common.DataModel.Repository MakeTestRepo(Common.DataModel.ShipHubContext context, long accountId, long repoId = 2001, string name = "myrepo") {
      context.BulkUpdateRepositories(DateTimeOffset.UtcNow, new[] {
        new RepositoryTableType() {
          Id = repoId,
          Name = name,
          FullName = "aroon/" + name,
          AccountId = accountId,
          Private = true,
        },
      }).GetAwaiter().GetResult();
      return context.Repositories.Single(x => x.Id == repoId);
    }

    public static Common.DataModel.Organization MakeTestOrg(Common.DataModel.ShipHubContext context, long orgId = 6001, string login = "myorg") {
      context.BulkUpdateAccounts(DateTimeOffset.UtcNow, new[] {
        new AccountTableType() {
          Id = orgId,
          Login = login,
          Type = "org",
        },
      }).GetAwaiter().GetResult();
      return context.Organizations.Single(x => x.Id == orgId);
    }
  }
}