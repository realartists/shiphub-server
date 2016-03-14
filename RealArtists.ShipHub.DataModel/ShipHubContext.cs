namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.Data;
  using System.Data.Common;
  using System.Data.Entity;
  using System.Data.SqlClient;
  using System.Linq;
  using System.Threading.Tasks;

  public class ShipHubContext : DbContext {
    static ShipHubContext() {
      Database.SetInitializer<ShipHubContext>(null);
    }

    public ShipHubContext()
      : this("name=ShipHubContext") {
    }

    public ShipHubContext(string nameOrConnectionString)
      : base(nameOrConnectionString) {
    }

    public ShipHubContext(DbConnection existingConnection, bool contextOwnsConnection)
      : base(existingConnection, contextOwnsConnection) {
    }

    public virtual DbSet<AccessToken> AccessTokens { get; set; }
    public virtual DbSet<Account> Accounts { get; set; }
    public virtual DbSet<AuthenticationToken> AuthenticationTokens { get; set; }
    public virtual DbSet<Repository> Repositories { get; set; }

    public virtual IQueryable<User> Users { get { return Accounts.OfType<User>(); } }
    public virtual IQueryable<Organization> Organizations { get { return Accounts.OfType<Organization>(); } }

    public override int SaveChanges() {
      throw new NotImplementedException("Please use SaveChangesAsync instead.");
    }

    protected override void OnModelCreating(DbModelBuilder mb) {
      mb.Entity<Account>()
        .HasOptional(x => x.AccessToken)
        .WithRequired(x => x.Account)
        .WillCascadeOnDelete();

      mb.Entity<Account>()
        .HasMany(x => x.AuthenticationTokens)
        .WithRequired(x => x.Account)
        .WillCascadeOnDelete();

      mb.Entity<Account>()
        .HasMany(x => x.Repositories)
        .WithRequired(x => x.Account)
        .WillCascadeOnDelete();

      //mb.Entity<User>()
      //  .HasMany(x => x.SubscribedRepositories)
      //  .WithMany(x => x.SubscribedUsers)
      //  .Map(m => m.ToTable("RepositorySubscriptions").MapLeftKey("UserId").MapRightKey("RepositoryId"));

      mb.Entity<Account>()
        .Map<User>(m => m.Requires("Type").HasValue(Account.UserType))
        .Map<Organization>(m => m.Requires("Type").HasValue(Account.OrganizationType));

      mb.Entity<Organization>()
        .HasMany(x => x.Users)
        .WithMany(x => x.Organizations)
        .Map(m => m.ToTable("OrganizationMembers").MapLeftKey("OrganizationId").MapRightKey("UserId"));
    }

    public Task UpdateRateLimit(string token, int limit, int remaining, DateTimeOffset reset) {
      return Database.ExecuteSqlCommandAsync(
        "EXEC [dbo].[UpdateRateLimit] @Token = @Token, @RateLimit = @Limit, @RateLimitRemaining = @Remaining, @RateLimitReset = @Reset",
        new SqlParameter("Token", SqlDbType.NVarChar, 64) { Value = token },
        new SqlParameter("Limit", SqlDbType.Int) { Value = limit },
        new SqlParameter("Remaining", SqlDbType.Int) { Value = remaining },
        new SqlParameter("Reset", SqlDbType.DateTimeOffset) { Value = reset });
    }

    public Task<int> BumpGlobalVersion(long minimum) {
      return Database.ExecuteSqlCommandAsync(
        "EXEC [dbo].[BumpGlobalVersion] @Minimum = @Minimum",
        new SqlParameter("Minimum", SqlDbType.BigInt) { Value = minimum });
    }

    public async Task<long> ReserveGlobalVersion(long rangeSize) {
      var result = new SqlParameter("Result", SqlDbType.Int) {
        Direction = ParameterDirection.Output
      };

      var rangeFirstValue = new SqlParameter("RangeFirstValue", SqlDbType.Variant) {
        Direction = ParameterDirection.Output
      };

      await Database.ExecuteSqlCommandAsync(
        @"EXEC @Result = [sys].[sp_sequence_get_range]
            @sequence_name = '[dbo].[SyncIdentifier]',
            @range_size = @RangeSize,
            @range_first_value = @RangeFirstValue OUTPUT;",
        result,
        rangeFirstValue,
        new SqlParameter("RangeSize", SqlDbType.BigInt) { Value = rangeSize });

      if (((int)result.Value) != 0) {
        throw new Exception($"Unable to reserve global version range of size {rangeSize}.");
      }

      return (long)rangeFirstValue.Value;
    }
  }
}
