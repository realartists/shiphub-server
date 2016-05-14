namespace GitHubUpdateProcessor {
  using System;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;
  using RealArtists.ShipHub.Common.DataModel;
  using gh = RealArtists.ShipHub.Common.GitHub.Models;

  public class DataLogic {
    private ShipHubContext _context;

    public DataLogic(ShipHubContext context) {
      _context = context;
    }

    public async Task<Account> UpdateOrStubAccount(gh.Account account, DateTimeOffset responseDate) {
      var existing = await _context.Accounts.SingleOrDefaultAsync(x => x.Id == account.Id);

      if (existing == null) {
        if (account.Type == gh.GitHubAccountType.Organization) {
          existing = _context.Accounts.Add(new Organization());
        } else {
          existing = _context.Accounts.Add(new User());
        }
        existing.Id = account.Id;
      }

      // This works for new additions because DatetTimeOffset defaults to its minimum value.
      if (existing.Date < responseDate) {
        SharedMapper.Map(account, existing);

        // TODO: Gross
        var trackingState = _context.ChangeTracker.Entries<Account>().Single(x => x.Entity.Id == account.Id);
        if (trackingState.State != EntityState.Unchanged) {
          existing.Date = responseDate;
        }
      }

      return existing;
    }

    public async Task<Repository> UpdateOrStubRepository(gh.Repository repo, DateTimeOffset responseDate) {
      var owner = await UpdateOrStubAccount(repo.Owner, responseDate);
      var existing = await _context.Repositories.SingleOrDefaultAsync(x => x.Id == repo.Id);

      if (existing == null) {
        existing = _context.Repositories.Add(new Repository() {
          Id = repo.Id,
        });
      }

      existing.Account = owner;
      SharedMapper.Map(repo, existing);

      return existing;
    }
  }
}
