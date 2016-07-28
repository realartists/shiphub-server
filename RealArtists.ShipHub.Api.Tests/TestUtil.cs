using System;

namespace RealArtists.ShipHub.Api.Tests {
  public class TestUtil {
    public static Common.DataModel.User MakeTestUser(Common.DataModel.ShipHubContext context) {
      var user = new Common.DataModel.User() {
        Id = 3001,
        Login = "aroon",
        Date = DateTimeOffset.Now,
        Token = Guid.NewGuid().ToString(),
      };
      context.Accounts.Add(user);
      return user;
    }

    public static Common.DataModel.Repository MakeTestRepo(Common.DataModel.ShipHubContext context, long accountId, long repoId = 2001, string name = "myrepo") {
      var repo = new Common.DataModel.Repository() {
        Id = repoId,
        Name = name,
        FullName = "aroon/" + name,
        AccountId = accountId,
        Private = true,
        Date = DateTimeOffset.Now,
      };
      return context.Repositories.Add(repo);
    }

    public static Common.DataModel.Organization MakeTestOrg(Common.DataModel.ShipHubContext context) {
      return (Common.DataModel.Organization)context.Accounts.Add(new Common.DataModel.Organization() {
        Id = 6001,
        Login = "myorg",
        Date = DateTimeOffset.Now,
      });
    }
  }
}