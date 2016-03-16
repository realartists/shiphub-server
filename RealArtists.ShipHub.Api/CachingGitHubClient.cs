namespace RealArtists.ShipHub.Api {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Web;
  using DataModel;
  using RealArtists.GitHub;
  using Utilities;
  using System.Data.Entity;

  public class CachingGitHubClient : IDisposable {
    private int _userId;
    private ShipHubContext _db = new ShipHubContext();

    private GitHubClient _gh;
    private GitHubClient _GitHub {
      get {
        return _gh ?? (_gh = CreateGitHubClient());
      }
    }

    private User _user;
    private User _User {
      get {
        return _user ?? (_user = (User)_db.Accounts.Include(x => x.AccessToken).Single(x => x.Id == _userId));
      }
    }

    public CachingGitHubClient(int userId) {
      _userId = userId;
    }

    //public async Task<Account> User() {
    //  var current = _User;
    //  var response = _GitHub.AuthenticatedUser(_User.ETag, _user.LastModified);
    //  if(response.
    //}

    //public async Task<Account> User(string login) {
    //}

    private GitHubClient CreateGitHubClient() {
      return GitHubSettings.CreateUserClient(_User.AccessToken.Token);
    }

    private bool disposedValue = false; // To detect redundant calls

    protected virtual void Dispose(bool disposing) {
      if (!disposedValue) {
        if (disposing) {
          // Too cute?
          Interlocked.Exchange(ref _db, null)?.Dispose();
          Interlocked.Exchange(ref _gh, null)?.Dispose();
        }

        disposedValue = true;
      }
    }

    public void Dispose() {
      Dispose(true);
    }
  }
}