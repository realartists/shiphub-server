namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.Collections.Generic;
  using System.Data.Common;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;

  public class GitHubContext : DbContext {
    static GitHubContext() {
      Database.SetInitializer<ShipHubContext>(null);
    }

    public virtual DbSet<object> name { get; set; }

    public GitHubContext() 
      : this("name=ShipHubContext") {
    }

    public GitHubContext(string nameOrConnectionString) 
      : base(nameOrConnectionString) {
    }

    public GitHubContext(DbConnection existingConnection, bool contextOwnsConnection) 
      : base(existingConnection, contextOwnsConnection) {
    }

    public virtual DbSet<GitHubAccountModel> Accounts { get; set; }
    public virtual DbSet<GitHubAuthenticationTokenModel> AuthenticationTokens { get; set; }
    public virtual DbSet<GitHubRepositoryModel> Repositories { get; set; }

    public override int SaveChanges() {
      throw new NotImplementedException("Please use SaveChangesAsync instead.");
    }

    protected override void OnModelCreating(DbModelBuilder mb) {
      mb.Entity<GitHubAccountModel>()
        .HasMany(x => x.AuthenticationTokens)
        .WithRequired(x => x.Account)
        .WillCascadeOnDelete();

      mb.Entity<GitHubAccountModel>()
        .HasMany(x => x.Repositories)
        .WithRequired(x => x.Owner)
        .WillCascadeOnDelete();
    }
  }
}
