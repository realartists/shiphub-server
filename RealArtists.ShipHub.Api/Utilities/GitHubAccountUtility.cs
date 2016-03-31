namespace RealArtists.ShipHub.Api.Utilities {
  using System;
  using Ship = DataModel;
  using GitHub;
  using GitHub.Models;

  public static class GitHubAccountUtility {
    public static void UpdateCacheInfo(this Ship.IGitHubResource resource, Ship.ShipHubContext context, GitHubResponse response) {
      var meta = resource.MetaData;
      if (meta == null) {
        meta = context.GitHubMetaData.Add(new Ship.GitHubMetaData());
        resource.MetaData = meta;
      }
      meta.ETag = response.ETag;
      meta.Expires = response.Expires;
      meta.LastModified = response.LastModified;
      meta.LastRefresh = DateTimeOffset.UtcNow;
    }

    public static void UpdateAccount(this Ship.Account account, GitHubResponse<Account> response) {
      var user = response.Result;

      if (account.Id != user.Id) {
        throw new InvalidOperationException($"Cannot update GitHubAccount {account.Id} with data for {user.Id}");
      }

      account.AvatarUrl = user.AvatarUrl;
      account.Login = user.Login;
      account.Name = user.Name;
    }
  }
}