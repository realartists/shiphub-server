namespace RealArtists.ShipHub.Api {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web;
  using DataModel;
  using RealArtists.ShipHub.Api.GitHub;
  using Utilities;
  using System.Data.Entity;
  using System.Net;

  public static class GitHubCacheHelpers {
    public static ConditionalHeaders ConditionalHeaders(this IGitHubResource resource) {
      return new ConditionalHeaders(resource.ETag, resource.LastModified);
    }
  }

  public class CachingGitHubClient {
    private GitHubClient _gh;
    private ShipHubContext _db;
    private User _user;

    public CachingGitHubClient(ShipHubContext context, User user) {
      _gh = GitHubSettings.CreateUserClient(user.AccessToken.Token);
      _db = context;
      _user = user;
    }

    public async Task<User> CurrentUser() {
      var current = _user;
      var token = _user.AccessToken;

      _gh.Credentials = new GitHubOauthCredentials(token.Token);
      var updated = await _gh.AuthenticatedUser(current.ConditionalHeaders());

      current.CacheToken = token;
      current.LastRefresh = DateTimeOffset.UtcNow;

      token.RateLimit = updated.RateLimit;
      token.RateLimitRemaining = updated.RateLimitRemaining;
      token.RateLimitReset = updated.RateLimitReset;

      if (updated.Status != HttpStatusCode.NotModified) {
        var u = updated.Result;
        if (u.Id != _user.Id) {
          throw new InvalidOperationException($"Refreshing current user cannot alter user id.");
        }

        current.ETag = updated.ETag;
        current.Expires = updated.Expires;
        current.LastModified = updated.LastModified;

        current.AvatarUrl = u.AvatarUrl;
        current.ExtensionJson = u.ExtensionJson;
        current.Login = u.Login;
        current.Name = u.Name;
      }

      await _db.SaveChangesAsync();
      return current;
    }

    // TODO: Is user id more robust when known?
    public async Task<User> User(string login) {
      var current = await _db.Users.SingleOrDefaultAsync(x => x.Login == login);
      var token = current?.AccessToken ?? _user.AccessToken;

      _gh.Credentials = new GitHubOauthCredentials(token.Token);
      var updated = await _gh.User(login, current.ConditionalHeaders());

      current.CacheToken = token;
      current.LastRefresh = DateTimeOffset.UtcNow;

      token.RateLimit = updated.RateLimit;
      token.RateLimitRemaining = updated.RateLimitRemaining;
      token.RateLimitReset = updated.RateLimitReset;

      if (updated.Status != HttpStatusCode.NotModified) {
        var u = updated.Result;
        if (u.Id != _user.Id) {
          throw new InvalidOperationException($"Refreshing current user cannot alter user id.");
        }

        current.ETag = updated.ETag;
        current.Expires = updated.Expires;
        current.LastModified = updated.LastModified;

        current.AvatarUrl = u.AvatarUrl;
        current.ExtensionJson = u.ExtensionJson;
        current.Login = u.Login;
        current.Name = u.Name;
      }

      await _db.SaveChangesAsync();
      return current;

    }
  }
}