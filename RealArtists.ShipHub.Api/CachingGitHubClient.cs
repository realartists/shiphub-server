namespace RealArtists.ShipHub.Api {
  using System;
  using System.Data.Entity;
  using System.Net;
  using System.Net.Http.Headers;
  using System.Threading.Tasks;
  using DataModel;
  using GitHub;
  using Utilities;

  public class GitHubResourceOptionWrapper : IGitHubRequestOptions, IGitHubCacheOptions, IGitHubCredentials {
    private IGitHubResource _resource;
    private IGitHubCredentials _fallbackCreds;

    public GitHubResourceOptionWrapper(IGitHubResource resource, IGitHubCredentials fallback = null) {
      _resource = resource;
      _fallbackCreds = fallback;
      var meta = resource.MetaData;
      var token = meta?.AccessToken.Token;
      if (token != null) {
        Scheme = "token";
        Parameter = token;
      } else {
        Scheme = fallback?.Scheme;
        Parameter = fallback?.Parameter;
      }
    }

    public IGitHubCacheOptions CacheOptions { get { return this; } }
    public IGitHubCredentials Credentials { get { return this; } }
    public string Scheme { get; private set; }
    public string Parameter { get; private set; }

    public string ETag { get { return _resource.MetaData?.ETag; } }
    public DateTimeOffset? LastModified { get { return _resource.MetaData?.LastModified; } }

    public void Apply(HttpRequestHeaders headers) {
      if (_resource.MetaData?.AccessToken != null) {
        headers.Authorization = new AuthenticationHeaderValue("token", _resource.MetaData?.AccessToken.Token);
      } else if (_fallbackCreds != null) {
        _fallbackCreds.Apply(headers);
      }
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
      _gh = GitHubSettings.CreateUserClient(user.PrimaryToken.Token);
      _db = context;
      _user = user;
    }

    public async Task<User> CurrentUser() {
      var current = _user;
      var token = _user.PrimaryToken;

      _gh.DefaultCredentials = GitHubCredentials.ForToken(token.Token);
      var updated = await _gh.AuthenticatedUser(current.ToGitHubRequestOptions());

      var meta = _user.MetaData;
      if (meta == null) {
        meta = _db.GitHubMetaData.Add(new GitHubMetaData());
        _user.MetaData = meta;
      }
      meta.AccessToken = token;
      meta.LastRefresh = DateTimeOffset.UtcNow;

      token.RateLimit = updated.RateLimit;
      token.RateLimitRemaining = updated.RateLimitRemaining;
      token.RateLimitReset = updated.RateLimitReset;

      if (updated.Status != HttpStatusCode.NotModified) {
        var u = updated.Result;
        if (u.Id != _user.Id) {
          throw new InvalidOperationException($"Refreshing current user cannot alter user id.");
        }

        meta.ETag = updated.ETag;
        meta.Expires = updated.Expires;
        meta.LastModified = updated.LastModified;

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
      var token = current?.PrimaryToken ?? _user.PrimaryToken;

      _gh.DefaultCredentials = GitHubCredentials.ForToken(token.Token);
      var updated = await _gh.User(login, current.ToGitHubRequestOptions());

      var meta = current.MetaData;
      if (meta == null) {
        meta = _db.GitHubMetaData.Add(new GitHubMetaData());
        current.MetaData = meta;
      }
      meta.AccessToken = token;
      meta.LastRefresh = DateTimeOffset.UtcNow;

      token.RateLimit = updated.RateLimit;
      token.RateLimitRemaining = updated.RateLimitRemaining;
      token.RateLimitReset = updated.RateLimitReset;

      if (updated.Status != HttpStatusCode.NotModified) {
        var u = updated.Result;
        if (u.Id != _user.Id) {
          throw new InvalidOperationException($"Refreshing current user cannot alter user id.");
        }

        meta.ETag = updated.ETag;
        meta.Expires = updated.Expires;
        meta.LastModified = updated.LastModified;

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