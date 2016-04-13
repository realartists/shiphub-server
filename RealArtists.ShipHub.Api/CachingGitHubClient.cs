namespace RealArtists.ShipHub.Api {
  using System;
  using System.Collections.Generic;
  using System.Data.Entity;
  using System.Linq;
  using System.Linq.Expressions;
  using System.Net;
  using System.Net.Http.Headers;
  using System.Threading.Tasks;
  using AutoMapper;
  using DataModel;
  using GitHub;

  public class GitHubResourceOptionWrapper<T> : IGitHubRequestOptions, IGitHubCacheOptions, IGitHubCredentials
    where T : IGitHubResource {

    private GitHubMetaData _metaData;
    private AuthenticationHeaderValue _authHeader;

    public GitHubResourceOptionWrapper(T resource, Func<T, GitHubMetaData> metaDataSelector = null) {
      if (resource != null) {
        _metaData = metaDataSelector == null ? resource.MetaData : metaDataSelector(resource);

        var token = _metaData?.AccessToken.Token;
        if (token != null) {
          _authHeader = new AuthenticationHeaderValue("token", token);
        }
      }
    }

    public IGitHubCacheOptions CacheOptions { get { return _metaData == null ? null : this; } }
    public IGitHubCredentials Credentials { get { return _authHeader == null ? null : this; } }

    public string Scheme { get { return _authHeader.Scheme; } }
    public string Parameter { get { return _authHeader.Parameter; } }

    public string ETag { get { return _metaData.ETag; } }
    public DateTimeOffset? LastModified { get { return _metaData.LastModified; } }

    public void Apply(HttpRequestHeaders headers) {
      headers.Authorization = _authHeader;
    }
  }

  public static class GitHubCacheHelpers {
    public static IGitHubRequestOptions ToGitHubRequestOptions<T>(
      this T resource,
      Func<T, GitHubMetaData> metaDataSelector = null)
      where T: IGitHubResource {
      return new GitHubResourceOptionWrapper<T>(resource, metaDataSelector);
    }
  }

  public class CachingGitHubClient {
    private IMapper Mapper { get { return AutoMapperConfig.Mapper; } }

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
      var updated = await _gh.User();

      // DO NOT UPDATE ANY METADATA - accounts are refreshed at a different endpoint, and eTags won't match, even if token does.

      _token.UpdateRateLimits(updated);

      if (updated.IsError) {
        throw updated.Error.ToException();
      }

      var u = updated.Result;
      if (u.Id != current.Id) {
        throw new InvalidOperationException("Refreshing current user cannot alter user id.");
      }

      Mapper.Map(updated, current);

      await _db.SaveChangesAsync();
      return current;
    }

    public async Task<User> User(string login) {
      var current = await _db.Users
        .Include(x => x.MetaData)
        .SingleOrDefaultAsync(x => x.Login == login);

      var updated = await _gh.User(login, current.ToGitHubRequestOptions());
      if (updated.IsError) {
        throw updated.Error.ToException();
      }

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

    public async Task<IEnumerable<Repository>> Repositories() {
      var user = _user;
      await EnsureLoaded(user, x => x.LinkedRepositories);

      var response = await _gh.Repositories(user.ToGitHubRequestOptions(x => x.RepositoryMetaData));
      if (response.IsError) {
        throw response.Error.ToException();
      }

      //if (response.Status == HttpStatusCode.NotModified) {
      //  user.RepositoryMetaData.
      //}

      var currentRepoIds = user.LinkedRepositories.Select(x => x.Id).ToHashSet();
      var updatedRepoIds = response.Result.Select(x => x.Id).ToHashSet();

      // Do not update existing repositories. That happens on demand or when scheduled
      foreach (var added in response.Result.Where(x => !currentRepoIds.Contains(x.Id))) {
      }

      // TODO: Schedule for GC check?
      foreach (var removed in user.LinkedRepositories.Where(x => !updatedRepoIds.Contains(x.Id))) {
        user.LinkedRepositories.Remove(removed);
      }

      await _db.SaveChangesAsync();

      return user.LinkedRepositories;
    }

    private async Task EnsureLoaded<TEntity, TElement>(TEntity entity, Expression<Func<TEntity, ICollection<TElement>>> navigationProperty)
      where TEntity : class
      where TElement : class {
      var entry = _db.Entry(entity);
      if ((entry.State & (EntityState.Added | EntityState.Detached)) == 0) {
        var relationship = entry.Collection(navigationProperty);
        if (!relationship.IsLoaded) {
          await relationship.LoadAsync();
        }
      }
    }
  }
}