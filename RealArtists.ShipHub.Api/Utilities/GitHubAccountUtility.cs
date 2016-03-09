namespace RealArtists.ShipHub.Api.Utilities {
  using System;
  using DataModel;
  using GitHub;
  using GitHub.Models;

  public static class GitHubAccountUtility {
    public static void Update(this GitHubAccountModel account, GitHubResponse<User> response) {
      var user = response.Result;

      if (account.Id != user.Id) {
        throw new InvalidOperationException($"Cannot update GitHubAccount {account.Id} with data for {user.Id}");
      }

      account.AvatarUrl = user.AvatarUrl;
      account.Company = user.Company;
      account.CreatedAt = user.CreatedAt;
      account.Login = user.Login;
      account.Name = user.Name;
      account.UpdatedAt = user.UpdatedAt;
      account.ETag = response.ETag;
      account.Expires = response.Expires;
      account.LastModified = response.LastModified;
      account.LastRefresh = DateTimeOffset.UtcNow;
    }
  }
}