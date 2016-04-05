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
      var token = meta?.AccessToken?.Token;
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
      if (resource == null || resource.MetaData == null)
        return null;

      return new GitHubResourceOptionWrapper(resource);
    }
  }

  public class CachingGitHubClient {
    private GitHubClient _gh;
    private ShipHubContext _db;
    private User _user;
    private AccessToken _token;

    public CachingGitHubClient(ShipHubContext context, User user, AccessToken token) {
      if (context == null) {
        throw new ArgumentNullException(nameof(context));
      }
      if (user == null) {
        throw new ArgumentNullException(nameof(user));
      }
      if (token == null) {
        throw new ArgumentNullException(nameof(token));
      }
      if (user.Id != token.AccountId) {
        throw new ArgumentException("Token provided is for a different user.", nameof(token));
      }

      _db = context;
      _user = user;
      _token = token;
      _gh = GitHubSettings.CreateUserClient(token.Token);
    }

    public async Task<User> CurrentUser() {
      var current = _user;

      // DO NOT SEND ANY OPTIONS - we want to ensure we use the default credentials.
      var updated = await _gh.AuthenticatedUser();

      // DO NOT UPDATE ANY METADATA - accounts are refreshed at a different endpoint, and eTags won't match, even if token does.

      _token.UpdateRateLimits(updated);

      if (updated.IsError) {
        throw updated.Error.ToException();
      }

      var u = updated.Result;
      if (u.Id != current.Id) {
        throw new InvalidOperationException("Refreshing current user cannot alter user id.");
      }

      current.AvatarUrl = u.AvatarUrl;
      current.ExtensionJson = u.ExtensionJson;
      current.Login = u.Login;
      current.Name = u.Name;

      await _db.SaveChangesAsync();
      return current;
    }

    public async Task<User> User(string login) {
      var current = await _db.Users
        .Include(x => x.MetaData)
        .SingleOrDefaultAsync(x => x.Login == login);

      var updated = await _gh.User(login, current.ToGitHubRequestOptions());

      var meta = current.MetaData;
      if (meta == null) {
        meta = _db.GitHubMetaData.Add(new GitHubMetaData());
        current.MetaData = meta;
      }
      meta.LastRefresh = DateTimeOffset.UtcNow;

      if (updated.Credentials.Parameter != meta.AccessToken?.Token) {
        meta.AccessToken = await _db.AccessTokens.SingleAsync(x => x.Token == updated.Credentials.Parameter);
      }
      meta.AccessToken.UpdateRateLimits(updated);

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