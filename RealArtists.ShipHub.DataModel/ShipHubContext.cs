namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.Data.Common;
  using System.Data.Entity;
  using System.Linq;

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
  }
}
