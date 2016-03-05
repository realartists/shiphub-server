namespace RealArtists.ShipHub.DataModel {
  using System;
  using System.Data.Common;
  using System.Data.Entity;

  public class GitHubContext : DbContext {
    static GitHubContext() {
      Database.SetInitializer<GitHubContext>(null);
    }

    public GitHubContext()
      : this("name=GitHubContext") {
    }

    public GitHubContext(string nameOrConnectionString)
      : base(nameOrConnectionString) {
    }

    public GitHubContext(DbConnection existingConnection, bool contextOwnsConnection)
      : base(existingConnection, contextOwnsConnection) {
    }

    public virtual DbSet<GitHubAccountModel> Accounts { get; set; }
    public virtual DbSet<GitHubAccessTokenModel> AccessTokens { get; set; }
    public virtual DbSet<GitHubRepositoryModel> Repositories { get; set; }

    public override int SaveChanges() {
      throw new NotImplementedException("Please use SaveChangesAsync instead.");
    }

    protected override void OnModelCreating(DbModelBuilder mb) {
      mb.Entity<GitHubAccountModel>()
        .HasOptional(x => x.AccessToken)
        .WithRequired(x => x.Account)
        .WillCascadeOnDelete();

      mb.Entity<GitHubAccountModel>()
        .HasMany(x => x.Repositories)
        .WithRequired(x => x.Owner)
        .WillCascadeOnDelete();
    }
  }
}
