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
  using Common;
  using Common.DataModel;
  using Common.GitHub;

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
      where T : IGitHubResource {
      return new GitHubResourceOptionWrapper<T>(resource, metaDataSelector);
    }
  }

  public class GitHubSpider {
    private IMapper Mapper { get { return AutoMapperConfig.Mapper; } }

    private GitHubClient _gh;
    private ShipHubContext _db;
    private User _user;
    private AccessToken _token;

    public GitHubSpider(ShipHubContext context, User user, AccessToken token) {
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

    //public async Task<User> RefreshCurrentUser() {
    //  // DO NOT SEND ANY OPTIONS - we want to ensure we use the default credentials.
    //  var response = await _gh.User();
    //  if (response.IsError) {
    //    throw response.Error.ToException();
    //  }

    //  // DO NOT UPDATE ANY METADATA - accounts are refreshed at a different endpoint, and eTags won't match, even if token does.
    //  // Just update token rate limits.
    //  _token.RateLimit = response.RateLimit;
    //  _token.RateLimitRemaining = response.RateLimitRemaining;
    //  _token.RateLimitReset = response.RateLimitReset;

    //  // Update the user
    //  Mapper.Map(response.Result, _user);

    //  await _db.SaveChangesAsync();
    //  return _user;
    //}

    /// <summary>
    /// We don't need a "Current User" variant of this since all the fields we care about are public.
    /// </summary>
    /// <param name="login">The GitHub login of the user to refresh.</param>
    /// <returns></returns>
    public async Task<User> RefreshUser(string login) {
      var user = await _db.Users
        .Include(x => x.MetaData)
        .SingleOrDefaultAsync(x => x.Login == login);

      var response = await _gh.User(login, user.ToGitHubRequestOptions());
      user = await UpdateEntity(response, user, newResource: () => _db.Accounts.New<Account, User>());

      await _db.SaveChangesAsync();
      return user;
    }

    public async Task<Organization> RefreshOrganization(string login) {
      var org = await _db.Organizations
        .Include(x => x.MetaData)
        .SingleOrDefaultAsync(x => x.Login == login);

      var response = await _gh.Organization(login, org.ToGitHubRequestOptions());
      org = await UpdateEntity(response, org, newResource: () => _db.Accounts.New<Account, Organization>());

      await _db.SaveChangesAsync();
      return org;
    }

    public async Task<IEnumerable<Repository>> RefreshRepositories() {
      var user = _user;
      await EnsureLoaded(user, x => x.LinkedRepositories);

      var response = await _gh.Repositories(user.ToGitHubRequestOptions(x => x.RepositoryMetaData));
      //FUUUUUUUUUUUUUCK


      if (response.Status != HttpStatusCode.NotModified) {
        var currentRepoIds = user.LinkedRepositories.Select(x => x.Id).ToHashSet();
        var updatedRepoIds = response.Result.Select(x => x.Id).ToHashSet();

        // TODO: Schedule for GC check?
        foreach (var removed in user.LinkedRepositories.Where(x => !updatedRepoIds.Contains(x.Id))) {
          user.LinkedRepositories.Remove(removed);
        }

        // Do not update existing repositories. That happens on demand or when scheduled
        foreach (var added in response.Result.Where(x => !currentRepoIds.Contains(x.Id))) {
          //var details = Repository(
        }
      }

      await _db.SaveChangesAsync();

      return user.LinkedRepositories;
    }

    public async Task<Repository> Repository(string fullName) {
      var current = await _db.Repositories
        .Include(x => x.MetaData)
        .SingleOrDefaultAsync(x => x.FullName == fullName);

      var response = await _gh.Repository(fullName);
      if (response.IsError) {
        throw response.Error.ToException();
      }

      if (current == null) {
        current = _db.Repositories.Add(_db.Repositories.Create());
      }

      var meta = current.MetaData;
      if (meta == null) {
        meta = _db.GitHubMetaData.Add(new GitHubMetaData());
        current.MetaData = meta;
      }
      meta.LastRefresh = DateTimeOffset.UtcNow;

      if (response.Credentials.Parameter != meta.AccessToken?.Token) {
        meta.AccessToken = await _db.AccessTokens.SingleAsync(x => x.Token == response.Credentials.Parameter);
      }
      meta.AccessToken.UpdateRateLimits(response);

      if (response.Status != HttpStatusCode.NotModified) {
      }

      await _db.SaveChangesAsync();

      return current;
    }

    private async Task<TEntity> UpdateEssentials<TEntity>(
        GitHubResponse response,
        TEntity resource,
        Func<TEntity> newResource = null,
        Func<TEntity, GitHubMetaData> metaGetter = null,
        Action<TEntity, GitHubMetaData> metaSetter = null)
      where TEntity : class, IGitHubResource {
      if (response == null) {
        throw new ArgumentNullException(nameof(response));
      }
      if (response.IsError) {
        throw response.Error.ToException();
      }

      newResource = newResource ?? (() => _db.Set<TEntity>().New());
      metaGetter = metaGetter ?? (entity => entity.MetaData);
      metaSetter = metaSetter ?? ((entity, meta) => entity.MetaData = meta);

      // Create resource if needed
      if (resource == null) {
        resource = newResource();
      }

      // MetaData
      var m = metaGetter(resource);
      if (m == null) {
        m = _db.GitHubMetaData.New();
        metaSetter(resource, m);
      }

      m.ETag = response.ETag;
      m.Expires = response.Expires;
      m.LastModified = response.LastModified;
      m.LastRefresh = DateTimeOffset.UtcNow;

      // Update token if needed
      var parameter = response.Credentials.Parameter;
      if (parameter != m.AccessToken?.Token) {
        // It shouldn't be possible for this lookup to fail.
        m.AccessToken = await _db.AccessTokens.SingleAsync(x => x.Token == parameter);
      }
      var token = m.AccessToken;

      token.RateLimit = response.RateLimit;
      token.RateLimitRemaining = response.RateLimitRemaining;
      token.RateLimitReset = response.RateLimitReset;

      return resource;
    }

    private async Task<TEntity> UpdateEntity<TEntity, TResponse>(
        GitHubResponse<TResponse> response,
        TEntity resource,
        Func<TEntity> newResource = null,
        Func<TEntity, GitHubMetaData> metaGetter = null,
        Action<TEntity, GitHubMetaData> metaSetter = null)
      where TEntity : class, IGitHubResource {
      var updated = await UpdateEssentials(response, resource, newResource, metaGetter, metaSetter);

      // Finally, map the result itself
      if (response.Status != HttpStatusCode.NotModified) {
        Mapper.Map(response.Result, updated);
      }

      return updated;
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