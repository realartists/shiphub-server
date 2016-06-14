namespace RealArtists.ShipHub.Common.Dump {
  using System;
  using System.Data.Entity;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Linq;

  public partial class Model1 : DbContext {
    public Model1()
        : base("name=Model1") {
    }

    public virtual DbSet<AccessToken> AccessTokens { get; set; }
    public virtual DbSet<AccountRepository> AccountRepositories { get; set; }
    public virtual DbSet<Account> Accounts { get; set; }
    public virtual DbSet<Comment> Comments { get; set; }
    public virtual DbSet<GitHubMetaData> GitHubMetaDatas { get; set; }
    public virtual DbSet<IssueEvent> IssueEvents { get; set; }
    public virtual DbSet<Issue> Issues { get; set; }
    public virtual DbSet<Label> Labels { get; set; }
    public virtual DbSet<Milestone> Milestones { get; set; }
    public virtual DbSet<Repository> Repositories { get; set; }
    public virtual DbSet<RepositoryLog> RepositoryLogs { get; set; }
    public virtual DbSet<Hook> Hooks { get; set; }

    protected override void OnModelCreating(DbModelBuilder modelBuilder) {
      modelBuilder.Entity<Account>()
          .HasMany(e => e.AccessTokens)
          .WithRequired(e => e.Account)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
          .HasMany(e => e.AccountRepositories)
          .WithRequired(e => e.Account)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
          .HasMany(e => e.Comments)
          .WithRequired(e => e.Account)
          .HasForeignKey(e => e.UserId)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
          .HasMany(e => e.Issues)
          .WithOptional(e => e.Account)
          .HasForeignKey(e => e.AssigneeId);

      modelBuilder.Entity<Account>()
          .HasMany(e => e.Issues1)
          .WithOptional(e => e.Account1)
          .HasForeignKey(e => e.ClosedById);

      modelBuilder.Entity<Account>()
          .HasMany(e => e.Issues2)
          .WithRequired(e => e.Account2)
          .HasForeignKey(e => e.UserId)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
          .HasMany(e => e.Repositories)
          .WithRequired(e => e.Account)
          .HasForeignKey(e => e.AccountId)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
          .HasMany(e => e.Accounts1)
          .WithMany(e => e.Accounts)
          .Map(m => m.ToTable("AccountOrganizations").MapLeftKey("OrganizationId").MapRightKey("UserId"));

      modelBuilder.Entity<Account>()
          .HasMany(e => e.Repositories1)
          .WithMany(e => e.Accounts)
          .Map(m => m.ToTable("RepositoryAccounts").MapLeftKey("AccountId").MapRightKey("RepositoryId"));

      modelBuilder.Entity<GitHubMetaData>()
          .HasMany(e => e.Accounts)
          .WithOptional(e => e.GitHubMetaData)
          .HasForeignKey(e => e.RepositoryMetaDataId);

      modelBuilder.Entity<GitHubMetaData>()
          .HasMany(e => e.Issues)
          .WithOptional(e => e.GitHubMetaData)
          .HasForeignKey(e => e.MetaDataId);

      modelBuilder.Entity<GitHubMetaData>()
          .HasMany(e => e.Repositories)
          .WithOptional(e => e.GitHubMetaData)
          .HasForeignKey(e => e.AssignableMetaDataId);

      modelBuilder.Entity<GitHubMetaData>()
          .HasMany(e => e.Repositories1)
          .WithOptional(e => e.GitHubMetaData1)
          .HasForeignKey(e => e.LabelMetaDataId);

      modelBuilder.Entity<Issue>()
          .HasMany(e => e.Comments)
          .WithRequired(e => e.Issue)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Issue>()
          .HasMany(e => e.Labels)
          .WithMany(e => e.Issues)
          .Map(m => m.ToTable("IssueLabels").MapLeftKey("IssueId").MapRightKey("LabelId"));

      modelBuilder.Entity<Label>()
          .HasMany(e => e.Repositories)
          .WithMany(e => e.Labels)
          .Map(m => m.ToTable("RepositoryLabels").MapLeftKey("LabelId").MapRightKey("RepositoryId"));

      modelBuilder.Entity<Repository>()
          .HasMany(e => e.AccountRepositories)
          .WithRequired(e => e.Repository)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Repository>()
          .HasMany(e => e.Comments)
          .WithRequired(e => e.Repository)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Repository>()
          .HasMany(e => e.IssueEvents)
          .WithRequired(e => e.Repository)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Repository>()
          .HasMany(e => e.Issues)
          .WithRequired(e => e.Repository)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Repository>()
          .HasMany(e => e.Milestones)
          .WithRequired(e => e.Repository)
          .WillCascadeOnDelete(false);

      modelBuilder.Entity<Repository>()
          .HasMany(e => e.RepositoryLogs)
          .WithRequired(e => e.Repository)
          .WillCascadeOnDelete(false);
    }
  }
}
