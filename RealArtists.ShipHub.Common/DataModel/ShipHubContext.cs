namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Collections.Generic;
  using System.Data;
  using System.Data.Common;
  using System.Data.Entity;
  using System.Data.SqlClient;
  using System.Linq;
  using System.Threading.Tasks;
  using Types;

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
    public virtual DbSet<AccountRepository> AccountRepositories { get; set; }
    public virtual DbSet<Account> Accounts { get; set; }
    public virtual DbSet<Comment> Comments { get; set; }
    public virtual DbSet<GitHubMetaData> GitHubMetaData { get; set; }
    public virtual DbSet<IssueEvent> IssueEvents { get; set; }
    public virtual DbSet<Issue> Issues { get; set; }
    public virtual DbSet<Label> Labels { get; set; }
    public virtual DbSet<Milestone> Milestones { get; set; }
    public virtual DbSet<Repository> Repositories { get; set; }
    public virtual DbSet<RepositoryLogEntry> RepositoryLogs { get; set; }

    public virtual IQueryable<User> Users { get { return Accounts.OfType<User>(); } }
    public virtual IQueryable<Organization> Organizations { get { return Accounts.OfType<Organization>(); } }

    public override int SaveChanges() {
      throw new NotImplementedException("Please use SaveChangesAsync instead.");
    }

    protected override void OnModelCreating(DbModelBuilder mb) {
      //mb.Entity<AccessToken>()
      //  .HasMany(e => e.MetaData)
      //  .WithOptional(e => e.AccessToken)
      //  .WillCascadeOnDelete();

      mb.Entity<Account>()
        .Map<User>(m => m.Requires("Type").HasValue(Account.UserType))
        .Map<Organization>(m => m.Requires("Type").HasValue(Account.OrganizationType));

      mb.Entity<Account>()
        .HasMany(e => e.AccessTokens)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.Comments)
        .WithRequired(e => e.User)
        .WillCascadeOnDelete(false);

      //mb.Entity<Account>()
      //  .HasMany(e => e.Events)
      //  .WithRequired(e => e.Actor)
      //  .HasForeignKey(e => e.ActorId)
      //  .WillCascadeOnDelete(false);

      //mb.Entity<Account>()
      //  .HasMany(e => e.AssigneeEvents)
      //  .WithRequired(e => e.Assignee)
      //  .HasForeignKey(e => e.AssigneeId)
      //  .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.AssignedIssues)
        .WithOptional(e => e.Assignee);

      mb.Entity<Account>()
        .HasMany(e => e.ClosedIssues)
        .WithOptional(e => e.ClosedBy);

      mb.Entity<Account>()
        .HasMany(e => e.Issues)
        .WithRequired(e => e.User)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.LinkedRepositories)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.OwnedRepositories)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

      mb.Entity<User>()
        .HasMany(e => e.Organizations)
        .WithMany(e => e.Members)
        .Map(m => m.ToTable("AccountOrganizations").MapLeftKey("UserId").MapRightKey("OrganizationId"));

      mb.Entity<Account>()
        .HasMany(e => e.AssignableRepositories)
        .WithMany(e => e.AssignableAccounts)
        .Map(m => m.ToTable("RepositoryAccounts").MapLeftKey("AccountId").MapRightKey("RepositoryId"));

      //mb.Entity<GitHubMetaData>()
      //  .HasMany(e => e.Issues)
      //  .WithOptional(e => e.MetaData)
      //  .HasForeignKey(e => e.MetaDataId);

      mb.Entity<Issue>()
        .HasMany(e => e.Comments)
        .WithRequired(e => e.Issue)
        .WillCascadeOnDelete(false);

      mb.Entity<Issue>()
        .HasMany(e => e.Labels)
        .WithMany(e => e.Issues)
        .Map(m => m.ToTable("IssueLabels").MapLeftKey("IssueId").MapRightKey("LabelId"));

      mb.Entity<Label>()
        .HasMany(e => e.Repositories)
        .WithMany(e => e.Labels)
        .Map(m => m.ToTable("RepositoryLabels").MapLeftKey("LabelId").MapRightKey("RepositoryId"));

      mb.Entity<Repository>()
        .HasMany(e => e.LinkedAccounts)
        .WithRequired(e => e.Repository)
        .WillCascadeOnDelete(false);

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

      mb.Entity<Repository>()
        .HasMany(e => e.Logs)
        .WithRequired(e => e.Repository)
        .WillCascadeOnDelete(false);
    }

    //public Task UpdateRateLimit(string token, int limit, int remaining, DateTimeOffset reset) {
    //  return Database.ExecuteSqlCommandAsync(
    //    "EXEC [dbo].[UpdateRateLimit] @Token = @Token, @RateLimit = @Limit, @RateLimitRemaining = @Remaining, @RateLimitReset = @Reset",
    //    new SqlParameter("Token", SqlDbType.NVarChar, 64) { Value = token },
    //    new SqlParameter("Limit", SqlDbType.Int) { Value = limit },
    //    new SqlParameter("Remaining", SqlDbType.Int) { Value = remaining },
    //    new SqlParameter("Reset", SqlDbType.DateTimeOffset) { Value = reset });
    //}

    public async Task BulkUpdateIssues(long repositoryId, IEnumerable<IssueTableType> issues, IEnumerable<LabelTableType> labels) {
      var issueParam = CreateTableParameter(
        "Issues",
        "[dbo].[IssueTableType]",
        new[] {
          Tuple.Create("Id", typeof(long)),
          Tuple.Create("UserId", typeof(long)),
          Tuple.Create("Number", typeof(int)),
          Tuple.Create("State", typeof(string)),
          Tuple.Create("Title", typeof(string)),
          Tuple.Create("Body", typeof(string)),
          Tuple.Create("AssigneeId", typeof(long)),
          Tuple.Create("MilestoneId", typeof(long)),
          Tuple.Create("Locked", typeof(bool)),
          Tuple.Create("CreatedAt", typeof(DateTimeOffset)),
          Tuple.Create("UpdatedAt", typeof(DateTimeOffset)),
          Tuple.Create("ClosedAt", typeof(DateTimeOffset)),
          Tuple.Create("ClosedById", typeof(long)),
          Tuple.Create("Reactions", typeof(string)),
        },
        x => new object[] {
          x.Id,
          x.UserId,
          x.Number,
          x.State,
          x.Title,
          x.Body,
          x.AssigneeId,
          x.MilestoneId,
          x.Locked,
          x.CreatedAt,
          x.UpdatedAt,
          x.ClosedAt,
          x.ClosedById,
          x.Reactions,
        },
        issues);

      var labelParam = CreateLabelTable("Labels", labels);

      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        "EXEC [dbo].[BulkUpdateIssues] @RepositoryId = @RepositoryId, @Issues = @Issues, @Labels = @Labels;",
        new SqlParameter("RepositoryId", SqlDbType.BigInt) { Value = repositoryId },
        issueParam,
        labelParam);
    }

    public async Task BulkUpdateComments(long repositoryId, IEnumerable<CommentTableType> comments) {
      var tableParam = CreateTableParameter(
        "Comments",
        "[dbo].[CommentTableType]",
        new[] {
          Tuple.Create("Id", typeof(long)),
          Tuple.Create("IssueNumber", typeof(int)),
          Tuple.Create("UserId", typeof(long)),
          Tuple.Create("Body", typeof(string)),
          Tuple.Create("CreatedAt", typeof(DateTimeOffset)),
          Tuple.Create("UpdatedAt", typeof(DateTimeOffset)),
          Tuple.Create("Reactions", typeof(string)),
        },
        x => new object[] {
          x.Id,
          x.IssueNumber,
          x.UserId,
          x.Body,
          x.CreatedAt,
          x.UpdatedAt,
          x.Reactions,
        },
        comments);

      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        "EXEC [dbo].[BulkUpdateComments] @RepositoryId = @RepositoryId, @Comments = @Comments;",
        new SqlParameter("RepositoryId", SqlDbType.BigInt) { Value = repositoryId },
        tableParam);
    }

    public async Task BulkUpdateIssueEvents(long repositoryId, IEnumerable<IssueEventTableType> issueEvents) {
      var tableParam = CreateTableParameter(
        "IssueEvents",
        "[dbo].[IssueEventTableType]",
        new[] {
          Tuple.Create("Id", typeof(long)),
          Tuple.Create("IssueId", typeof(long)),
          Tuple.Create("ActorId", typeof(long)),
          Tuple.Create("CommitId", typeof(string)),
          Tuple.Create("Event", typeof(string)),
          Tuple.Create("CreatedAt", typeof(DateTimeOffset)),
          Tuple.Create("AssigneeId", typeof(long)), // Nullable types handled by DataTable
          Tuple.Create("ExtensionData", typeof(string)),
        },
        x => new object[] {
          x.Id,
          x.IssueId,
          x.ActorId,
          x.CommitId,
          x.Event,
          x.CreatedAt,
          x.AssigneeId,
          x.ExtensionData,
        },
        issueEvents);

      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        "EXEC [dbo].[BulkUpdateIssueEvents] @RepositoryId = @RepositoryId, @IssueEvents = @IssueEvents;",
        new SqlParameter("RepositoryId", SqlDbType.BigInt) { Value = repositoryId },
        tableParam);
    }

    public async Task SetRepositoryLabels(long repositoryId, IEnumerable<LabelTableType> labels) {
      var tableParam = CreateLabelTable("Labels", labels);

      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        "EXEC [dbo].[SetRepositoryLabels] @RepositoryId = @RepositoryId, @Labels = @Labels;",
        new SqlParameter("RepositoryId", SqlDbType.BigInt) { Value = repositoryId },
        tableParam);
    }

    public async Task BulkUpdateAccounts(DateTimeOffset date, IEnumerable<AccountTableType> accounts) {
      var tableParam = CreateTableParameter(
        "Accounts",
        "[dbo].[AccountTableType]",
        new[] {
          Tuple.Create("Id", typeof(long)),
          Tuple.Create("Type", typeof(string)),
          Tuple.Create("Login", typeof(string)),
        },
        x => new object[] {
          x.Id,
          x.Type,
          x.Login,
        },
        accounts);

      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        "EXEC [dbo].[BulkUpdateAccounts] @Date = @Date, @Accounts = @Accounts;",
        new SqlParameter("Date", SqlDbType.DateTimeOffset) { Value = date },
        tableParam);
    }

    public async Task BulkUpdateMilestones(long repositoryId, IEnumerable<MilestoneTableType> milestones) {
      var tableParam = CreateTableParameter(
        "Milestones",
        "[dbo].[MilestoneTableType]",
        new[] {
          Tuple.Create("Id", typeof(long)),
          Tuple.Create("Number", typeof(int)),
          Tuple.Create("State", typeof(string)),
          Tuple.Create("Title", typeof(string)),
          Tuple.Create("Description", typeof(string)),
          Tuple.Create("CreatedAt", typeof(DateTimeOffset)),
          Tuple.Create("UpdatedAt", typeof(DateTimeOffset)),
          Tuple.Create("ClosedAt", typeof(DateTimeOffset)), // Nullable types handled by DataTable
          Tuple.Create("DueOn", typeof(DateTimeOffset)), // Nullable types handled by DataTable
        },
        x => new object[] {
          x.Id,
          x.Number,
          x.State,
          x.Title,
          x.Description,
          x.CreatedAt,
          x.UpdatedAt,
          x.ClosedAt,
          x.DueOn,
        },
        milestones);

      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        "EXEC [dbo].[BulkUpdateMilestones] @RepositoryId = @RepositoryId, @Milestones = @Milestones;",
        new SqlParameter("RepositoryId", SqlDbType.BigInt) { Value = repositoryId },
        tableParam);
    }

    public async Task BulkUpdateRepositories(DateTimeOffset date, IEnumerable<RepositoryTableType> repositories) {
      var tableParam = CreateTableParameter(
        "Repositories",
        "[dbo].[RepositoryTableType]",
        new[] {
          Tuple.Create("Id", typeof(long)),
          Tuple.Create("AccountId", typeof(long)),
          Tuple.Create("Private", typeof(bool)),
          Tuple.Create("Name", typeof(string)),
          Tuple.Create("FullName", typeof(string)),
        },
        x => new object[] {
          x.Id,
          x.AccountId,
          x.Private,
          x.Name,
          x.FullName,
        },
        repositories);

      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        "EXEC [dbo].[BulkUpdateRepositories] @Date = @Date, @Repositories = @Repositories;",
        new SqlParameter("Date", SqlDbType.DateTimeOffset) { Value = date },
        tableParam);
    }

    public async Task SetAccountLinkedRepositories(long accountId, IEnumerable<long> repositoryIds) {
      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        @"EXEC [dbo].[SetAccountLinkedRepositories]
          @AccountId = @AccountId,
          @RepositoryIds = @RepositoryIds;",
        new SqlParameter("AccountId", SqlDbType.BigInt) { Value = accountId },
        CreateItemListTable("RepositoryIds", repositoryIds));
    }

    public async Task SetUserOrganizations(long userId, IEnumerable<long> organizationIds) {
      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        @"EXEC [dbo].[SetUserOrganizations]
          @UserId = @UserId,
          @OrganizationIds = @OrganizationIds;",
        new SqlParameter("UserId", SqlDbType.BigInt) { Value = userId },
        CreateItemListTable("OrganizationIds", organizationIds));
    }

    public async Task SetOrganizationUsers(long organizationId, IEnumerable<long> userIds) {
      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        @"EXEC [dbo].[SetOrganizationUsers]
          @OrganizationId = @OrganizationId,
          @UserIds = @UserIds;",
        new SqlParameter("OrganizationId", SqlDbType.BigInt) { Value = organizationId },
        CreateItemListTable("UserIds", userIds));
    }

    public async Task SetRepositoryAssignableAccounts(long repositoryId, IEnumerable<long> assignableAccountIds) {
      await Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        @"EXEC [dbo].[SetRepositoryAssignableAccounts]
          @RepositoryId = @RepositoryId,
          @AssignableAccountIds = @AssignableAccountIds;",
        new SqlParameter("RepositoryId", SqlDbType.BigInt) { Value = repositoryId },
        CreateItemListTable("AssignableAccountIds", assignableAccountIds));
    }

    private static SqlParameter CreateItemListTable<T>(string parameterName, IEnumerable<T> values) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[ItemListTableType]",
        new[] { Tuple.Create("Item", typeof(T)) },
        x => new object[] { x },
        values);
    }

    private static SqlParameter CreateLabelTable(string parameterName, IEnumerable<LabelTableType> labels) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[LabelTableType]",
        new[] {
          Tuple.Create("ItemId", typeof(long)),
          Tuple.Create("Color", typeof(string)),
          Tuple.Create("Name", typeof(string)),
        },
        x => new object[] {
          x.Id,
          x.Color,
          x.Name,
        },
        labels);
    }

    private static SqlParameter CreateTableParameter<T>(string parameterName, string typeName, IEnumerable<Tuple<string, Type>> columns, Func<T, object[]> rowValues, IEnumerable<T> rows) {
      if (!typeName.Contains("[")) {
        typeName = $"[dbo].[{typeName}]";
      }

      DataTable table = null;

      if (rows != null) {
        table = new DataTable();

        table.Columns.AddRange(columns.Select(x => new DataColumn(x.Item1, x.Item2)).ToArray());

        foreach (var row in rows) {
          table.Rows.Add(rowValues(row));
        }
      }

      return new SqlParameter(parameterName, SqlDbType.Structured) {
        TypeName = typeName,
        Value = table
      };
    }
  }
}
