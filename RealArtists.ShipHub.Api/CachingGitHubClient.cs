namespace RealArtists.ShipHub.Api {
  using System;
  using System.Data.Entity;
  using System.Net;
  using System.Net.Http.Headers;
  using System.Threading.Tasks;
  using DataModel;
  using RealArtists.ShipHub.Api.GitHub;
  using Utilities;

  public class GitHubResourceOptionWrapper : IGitHubRequestOptions, IGitHubCacheOptions, IGitHubCredentials {
    private IGitHubResource _resource;

    public GitHubResourceOptionWrapper(IGitHubResource resource) {
      _resource = resource;
    }

    public IGitHubCacheOptions CacheOptions { get { return this; } }
    public IGitHubCredentials Credentials { get { return this; } }

    public string ETag { get { return _resource.ETag; } }
    public DateTimeOffset? LastModified { get { return _resource.LastModified; } }

    public void Apply(HttpRequestHeaders headers) {
      headers.Authorization = new AuthenticationHeaderValue("token", _resource.CacheToken.Token);
    }
  }

  public static class GitHubCacheHelpers {
    public static IGitHubRequestOptions ToGitHubRequestOptions(this IGitHubResource resource) {
      if (resource == null)
        return null;

      return new GitHubResourceOptionWrapper(resource);
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

      _gh.DefaultCredentials = new GitHubOauthCredentials(token.Token);
      var updated = await _gh.AuthenticatedUser(current.ToGitHubRequestOptions());

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

      _gh.DefaultCredentials = new GitHubOauthCredentials(token.Token);
      var updated = await _gh.User(login, current.ToGitHubRequestOptions());

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