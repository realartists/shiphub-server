namespace RealArtists.ShipHub.Api.AGModel {
  using System.Data.Entity;

  public partial class AGModel : DbContext {
    public AGModel()
        : base("name=AGModel") {
    }

    public virtual DbSet<AccessToken> AccessTokens { get; set; }
    public virtual DbSet<Account> Accounts { get; set; }
    public virtual DbSet<AuthenticationToken> AuthenticationTokens { get; set; }
    public virtual DbSet<Comment> Comments { get; set; }
    public virtual DbSet<Event> Events { get; set; }
    public virtual DbSet<GitHubMetaData> GitHubMetaDatas { get; set; }
    public virtual DbSet<Issue> Issues { get; set; }
    public virtual DbSet<Label> Labels { get; set; }
    public virtual DbSet<Milestone> Milestones { get; set; }
    public virtual DbSet<Repository> Repositories { get; set; }
    public virtual DbSet<PollQueueItem> PollQueueItems { get; set; }

    protected override void OnModelCreating(DbModelBuilder mb) {
      mb.Entity<AccessToken>()
        .HasMany(e => e.MetaData)
        .WithOptional(e => e.AccessToken)
        .WillCascadeOnDelete();

      mb.Entity<Account>()
        .HasMany(e => e.AccessTokens)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.Comments)
        .WithRequired(e => e.User)
        .HasForeignKey(e => e.UserId)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.Events)
        .WithRequired(e => e.Actor)
        .HasForeignKey(e => e.ActorId)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.AssigneeEvents)
        .WithRequired(e => e.Assignee)
        .HasForeignKey(e => e.AssigneeId)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.AssignedIssues)
        .WithOptional(e => e.Assignee)
        .HasForeignKey(e => e.AssigneeId);

      mb.Entity<Account>()
        .HasMany(e => e.ClosedIssues)
        .WithOptional(e => e.ClosedBy)
        .HasForeignKey(e => e.ClosedById);

      mb.Entity<Account>()
        .HasMany(e => e.Issues)
        .WithRequired(e => e.User)
        .HasForeignKey(e => e.UserId)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.Milestones)
        .WithRequired(e => e.User)
        .HasForeignKey(e => e.UserId)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.Repositories)
        .WithRequired(e => e.Account)
        .HasForeignKey(e => e.AccountId)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.Members)
        .WithMany(e => e.Organizations)
        .Map(m => m.ToTable("AccountOrganizations").MapLeftKey("OrganizationId").MapRightKey("UserId"));

      mb.Entity<Account>()
        .HasMany(e => e.AssignableRepositories)
        .WithMany(e => e.AssignableAccounts)
        .Map(m => m.ToTable("AccountRepositories").MapLeftKey("AccountId").MapRightKey("RepositoryId"));

      mb.Entity<GitHubMetaData>()
        .HasMany(e => e.Accounts)
        .WithOptional(e => e.MetaData)
        .HasForeignKey(e => e.MetaDataId);

      mb.Entity<GitHubMetaData>()
        .HasMany(e => e.Comments)
        .WithOptional(e => e.MetaData)
        .HasForeignKey(e => e.MetaDataId);

      mb.Entity<GitHubMetaData>()
        .HasMany(e => e.Issues)
        .WithOptional(e => e.MetaData)
        .HasForeignKey(e => e.MetaDataId);

      mb.Entity<GitHubMetaData>()
        .HasMany(e => e.Milestones)
        .WithOptional(e => e.MetaData)
        .HasForeignKey(e => e.MetaDataId);

      mb.Entity<Issue>()
        .HasMany(e => e.Comments)
        .WithRequired(e => e.Issue)
        .WillCascadeOnDelete(false);

      mb.Entity<Label>()
        .HasMany(e => e.Issues)
        .WithMany(e => e.Labels)
        .Map(m => m.ToTable("IssueLabels").MapLeftKey("LabelId").MapRightKey("IssueId"));

      mb.Entity<Label>()
        .HasMany(e => e.Repositories)
        .WithMany(e => e.Labels)
        .Map(m => m.ToTable("RepositoryLabels").MapLeftKey("LabelId").MapRightKey("RepositoryId"));

      mb.Entity<Repository>()
        .HasMany(e => e.Comments)
        .WithRequired(e => e.Repository)
        .WillCascadeOnDelete(false);

      mb.Entity<Repository>()
        .HasMany(e => e.Events)
        .WithRequired(e => e.Repository)
        .WillCascadeOnDelete(false);

      mb.Entity<Repository>()
        .HasMany(e => e.Issues)
        .WithRequired(e => e.Repository)
        .WillCascadeOnDelete(false);

      mb.Entity<Repository>()
        .HasMany(e => e.Milestones)
        .WithRequired(e => e.Repository)
        .WillCascadeOnDelete(false);
    }
  }
}
