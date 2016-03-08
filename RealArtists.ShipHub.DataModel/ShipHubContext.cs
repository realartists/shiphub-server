namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.Data.Common;
  using System.Data.Entity;

  public class ShipHubContext : GitHubContext {
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

    public virtual DbSet<ShipAuthenticationTokenModel> AuthenticationTokens { get; set; }
    public virtual DbSet<ShipUserModel> Users { get; set; }

    public override int SaveChanges() {
      throw new NotImplementedException("Please use SaveChangesAsync instead.");
    }

    protected override void OnModelCreating(DbModelBuilder mb) {
      base.OnModelCreating(mb);

      mb.Entity<ShipUserModel>()
        .HasMany(x => x.AuthenticationTokens)
        .WithRequired(x => x.User)
        .WillCascadeOnDelete();
    }
  }
}
