namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.Data.Common;
  using System.Data.Entity;

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
    public virtual DbSet<Organization> Organizations { get; set; }
    public virtual DbSet<Repository> Repositories { get; set; }
    public virtual DbSet<User> Users { get; set; }

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

      mb.Entity<Account>()
        .Map<User>(m => m.Requires("Type").HasValue("user"))
        .Map<Organization>(m => m.Requires("Type").HasValue("org"));

      mb.Entity<Organization>()
        .HasMany(x => x.Members)
        .WithMany(x => x.Organizations)
        .Map(m => m.ToTable("OrganizationMembers").MapLeftKey("OrganizationId").MapRightKey("UserId"));
    }
  }
}
