﻿namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Collections.Generic;
  using System.Data;
  using System.Data.Common;
  using System.Data.Entity;
  using System.Data.SqlClient;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Threading.Tasks;
  using GitHub;
  using Legacy;
  using Types;

  public class ShipHubContext : DbContext {
    static ShipHubContext() {
      // Tell EF to leave our DB alone.
      // Maybe do migrations with dacpacs when possible later.
      Database.SetInitializer<ShipHubContext>(null);
    }

    public ShipHubContext()
      : this("name=ShipHubContext") {
    }

    public ShipHubContext(string nameOrConnectionString)
      : base(nameOrConnectionString) {
      ConnectionFactory = new SqlConnectionFactory(Database.Connection.ConnectionString);
    }

    public ShipHubContext(DbConnection existingConnection, bool contextOwnsConnection)
      : base(existingConnection, contextOwnsConnection) {
      ConnectionFactory = new SqlConnectionFactory(Database.Connection.ConnectionString);
    }

    public virtual DbSet<AccountRepository> AccountRepositories { get; set; }
    public virtual DbSet<Account> Accounts { get; set; }
    public virtual DbSet<Comment> Comments { get; set; }
    public virtual DbSet<Hook> Hooks { get; set; }
    public virtual DbSet<IssueEvent> IssueEvents { get; set; }
    public virtual DbSet<Issue> Issues { get; set; }
    public virtual DbSet<Label> Labels { get; set; }
    public virtual DbSet<Milestone> Milestones { get; set; }
    public virtual DbSet<Repository> Repositories { get; set; }

    public virtual IQueryable<User> Users { get { return Accounts.OfType<User>(); } }
    public virtual IQueryable<Organization> Organizations { get { return Accounts.OfType<Organization>(); } }

    public SqlConnectionFactory ConnectionFactory { get; private set; }

    public override int SaveChanges() {
      throw new NotImplementedException("Please use asynchronous methods instead.");
    }

    protected override void OnModelCreating(DbModelBuilder modelBuilder) {
      modelBuilder.Entity<Account>()
        .Map<User>(m => m.Requires("Type").HasValue(Account.UserType))
        .Map<Organization>(m => m.Requires("Type").HasValue(Account.OrganizationType));

      modelBuilder.Entity<Account>()
        .HasMany(e => e.Comments)
        .WithRequired(e => e.User)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
        .HasMany(e => e.AssignedIssues)
        .WithOptional(e => e.Assignee);

      modelBuilder.Entity<Account>()
        .HasMany(e => e.ClosedIssues)
        .WithOptional(e => e.ClosedBy);

      modelBuilder.Entity<Account>()
        .HasMany(e => e.Issues)
        .WithRequired(e => e.User)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
        .HasMany(e => e.OwnedRepositories)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

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
        .HasMany(e => e.LinkedAccounts)
        .WithRequired(e => e.Repository)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Repository>()
        .HasMany(e => e.Comments)
        .WithRequired(e => e.Repository)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Repository>()
        .HasMany(e => e.Events)
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

      modelBuilder.Entity<User>()
        .HasMany(e => e.AssignableRepositories)
        .WithMany(e => e.AssignableAccounts)
        .Map(m => m.ToTable("RepositoryAccounts").MapLeftKey("AccountId").MapRightKey("RepositoryId"));

      modelBuilder.Entity<User>()
        .HasMany(e => e.LinkedRepositories)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<User>()
        .HasMany(e => e.Organizations)
        .WithMany(e => e.Members)
        .Map(m => m.ToTable("AccountOrganizations").MapLeftKey("UserId").MapRightKey("OrganizationId"));
    }

    public Task UpdateMetaLimit(string table, string column, long id, GitHubMetadata metadata, string accessToken, GitHubRateLimit limit) {
      return Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        $"UPDATE [{table}] SET [{column}] = @Metadata WHERE Id = @Id AND([{column}] IS NULL OR CAST(JSON_VALUE([{column}], '$.LastRefresh') as DATETIMEOFFSET) < CAST(JSON_VALUE(@Metadata, '$.LastRefresh') as DATETIMEOFFSET))"
        + "\n\nEXEC [dbo].[UpdateRateLimit] @Token = @Token, @RateLimit = @RateLimit, @RateLimitRemaining = @RateLimitRemaining, @RateLimitReset = @RateLimitReset",
        new SqlParameter("Id", SqlDbType.BigInt) { Value = id },
        new SqlParameter("Metadata", SqlDbType.NVarChar) { Value = metadata.SerializeObject() },
        new SqlParameter("Token", SqlDbType.NVarChar, 64) { Value = accessToken },
        new SqlParameter("RateLimit", SqlDbType.Int) { Value = limit.RateLimit },
        new SqlParameter("RateLimitRemaining", SqlDbType.Int) { Value = limit.RateLimitRemaining },
        new SqlParameter("RateLimitReset", SqlDbType.DateTimeOffset) { Value = limit.RateLimitReset });
    }

    public Task UpdateRateLimits(string accessToken, GitHubRateLimit limit) {
      return Database.ExecuteSqlCommandAsync(
        TransactionalBehavior.DoNotEnsureTransaction,
        "EXEC [dbo].[UpdateRateLimit] @Token = @Token, @RateLimit = @RateLimit, @RateLimitRemaining = @RateLimitRemaining, @RateLimitReset = @RateLimitReset",
        new SqlParameter("Token", SqlDbType.NVarChar, 64) { Value = accessToken },
        new SqlParameter("RateLimit", SqlDbType.Int) { Value = limit.RateLimit },
        new SqlParameter("RateLimitRemaining", SqlDbType.Int) { Value = limit.RateLimitRemaining },
        new SqlParameter("RateLimitReset", SqlDbType.DateTimeOffset) { Value = limit.RateLimitReset });
    }

    private async Task<ChangeSummary> ExecuteAndReadChanges(string procedureName, Action<dynamic> applyParams) {
      var result = new ChangeSummary();

      using (var dsp = new DynamicStoredProcedure(procedureName, ConnectionFactory)) {
        applyParams(dsp);

        using (var sdr = await dsp.ExecuteReaderAsync(CommandBehavior.SingleResult)) {
          dynamic ddr = sdr;
          while (sdr.Read()) {
            result.Add(ddr.OrganizationId, ddr.RepositoryId, ddr.UserId);
          }
        }
      }

      return result;
    }

    public Task<ChangeSummary> UpdateAccount(DateTimeOffset date, AccountTableType account, GitHubMetadata metadata) {
      return BulkUpdateAccounts(date, new[] { account }, metadata);
    }

    public Task<ChangeSummary> BulkUpdateAccounts(DateTimeOffset date, IEnumerable<AccountTableType> accounts) {
      return BulkUpdateAccounts(date, accounts, null);
    }

    private Task<ChangeSummary> BulkUpdateAccounts(DateTimeOffset date, IEnumerable<AccountTableType> accounts, GitHubMetadata metadata) {
      var accountsParam = CreateTableParameter(
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

      return ExecuteAndReadChanges("[dbo].[BulkUpdateAccounts]", x => {
        x.Date = date;
        x.Accounts = accountsParam;

        if (metadata != null) {
          x.Metadata = metadata.SerializeObject();
        }
      });
    }

    public Task<ChangeSummary> BulkUpdateComments(long repositoryId, IEnumerable<CommentTableType> comments, bool complete = false) {
      var tableParam = CreateCommentTable("Comments", comments);

      return ExecuteAndReadChanges("[dbo].[BulkUpdateComments]", x => {
        x.RepositoryId = repositoryId;
        x.Comments = tableParam;
        x.Complete = complete;
      });
    }

    public Task<ChangeSummary> BulkUpdateIssueComments(string repositoryFullName, int issueNumber, IEnumerable<CommentTableType> comments, bool complete = false) {
      var tableParam = CreateCommentTable("Comments", comments);

      return ExecuteAndReadChanges("[dbo].[BulkUpdateIssueComments]", x => {
        x.RepositoryFullName = repositoryFullName;
        x.IssueNumber = issueNumber;
        x.Comments = tableParam;
        x.Complete = complete;
      });
    }

    public Task<ChangeSummary> BulkUpdateIssueEvents(
      long userId,
      long repositoryId,
      IEnumerable<IssueEventTableType> issueEvents,
      IEnumerable<long> referencedAccounts) {

      var eventsParam = CreateTableParameter(
        "IssueEvents",
        "[dbo].[IssueEventTableType]",
        new[] {
          Tuple.Create("Id", typeof(long)),
          Tuple.Create("IssueId", typeof(long)),
          Tuple.Create("ActorId", typeof(long)),
          Tuple.Create("Event", typeof(string)),
          Tuple.Create("CreatedAt", typeof(DateTimeOffset)),
          Tuple.Create("Hash", typeof(Guid)),
          Tuple.Create("Restricted", typeof(bool)),
          Tuple.Create("ExtensionData", typeof(string)),
        },
        x => new object[] {
          x.Id,
          x.IssueId,
          x.ActorId,
          x.Event,
          x.CreatedAt,
          x.Hash,
          x.Restricted,
          x.ExtensionData,
        },
        issueEvents);

      var accountsParam = CreateItemListTable("ReferencedAccounts", referencedAccounts);

      return ExecuteAndReadChanges("[dbo].[BulkUpdateIssueEvents]", x => {
        x.UserId = userId;
        x.RepositoryId = repositoryId;
        x.IssueEvents = eventsParam;
        x.ReferencedAccounts = accountsParam;
      });
    }

    public Task<ChangeSummary> BulkUpdateIssues(long repositoryId, IEnumerable<IssueTableType> issues, IEnumerable<LabelTableType> labels) {
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
          Tuple.Create("PullRequest", typeof(bool)),
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
          x.PullRequest,
        },
        issues);

      var labelParam = CreateLabelTable("Labels", labels);

      return ExecuteAndReadChanges("[dbo].[BulkUpdateIssues]", x => {
        x.RepositoryId = repositoryId;
        x.Issues = issueParam;
        x.Labels = labelParam;
      });
    }

    public Task<ChangeSummary> BulkUpdateMilestones(long repositoryId, IEnumerable<MilestoneTableType> milestones) {
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

      return ExecuteAndReadChanges("[dbo].[BulkUpdateMilestones]", x => {
        x.RepositoryId = repositoryId;
        x.Milestones = tableParam;
      });
    }

    public Task<ChangeSummary> BulkUpdateReactions(long repositoryId, long issueId, long? commentId, IEnumerable<ReactionTableType> reactions) {
      var reactionsParam = CreateTableParameter(
        "Reactions",
        "[dbo].[ReactionTableType]",
        new[] {
          Tuple.Create("Id", typeof(long)),
          Tuple.Create("UserId", typeof(long)),
          Tuple.Create("Content", typeof(string)),
          Tuple.Create("CreatedAt", typeof(DateTimeOffset)),
        },
        x => new object[] {
          x.Id,
          x.UserId,
          x.Content,
          x.CreatedAt,
        },
        reactions);

      return ExecuteAndReadChanges("[dbo].[BulkUpdateReactions]", x => {
        x.RepositoryId = repositoryId;
        x.IssueId = issueId;
        x.CommentId = commentId;
        x.Reactions = reactionsParam;
      });
    }

    public Task<ChangeSummary> BulkUpdateRepositories(DateTimeOffset date, IEnumerable<RepositoryTableType> repositories) {
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

      return ExecuteAndReadChanges("[dbo].[BulkUpdateRepositories]", x => {
        x.Date = date;
        x.Repositories = tableParam;
      });
    }

    public Task<ChangeSummary> SetRepositoryLabels(long repositoryId, IEnumerable<LabelTableType> labels) {
      var labelParam = CreateLabelTable("Labels", labels);

      return ExecuteAndReadChanges("[dbo].[SetRepositoryLabels]", x => {
        x.RepositoryId = repositoryId;
        x.Labels = labelParam;
      });
    }

    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Whats")]
    public DynamicStoredProcedure PrepareWhatsNew(long userId, long pageSize, IEnumerable<VersionTableType> repoVersions, IEnumerable<VersionTableType> orgVersions) {
      var factory = new SqlConnectionFactory(Database.Connection.ConnectionString);
      DynamicStoredProcedure result = null;

      try {
        dynamic dsp = result = new DynamicStoredProcedure("[dbo].[WhatsNew]", factory);
        dsp.UserId = userId;
        dsp.PageSize = pageSize;
        dsp.RepositoryVersions = CreateVersionTableType("RepositoryVersions", repoVersions);
        dsp.OrganizationVersions = CreateVersionTableType("OrganizationVersions", orgVersions);
      } catch {
        if (result != null) {
          result.Dispose();
          result = null;
        }
        throw;
      }

      return result;
    }

    public Task<ChangeSummary> SetAccountLinkedRepositories(long accountId, IEnumerable<long> repositoryIds, GitHubMetadata metadata) {
      var repoParam = CreateItemListTable("RepositoryIds", repositoryIds);

      return ExecuteAndReadChanges("[dbo].[SetAccountLinkedRepositories]", x => {
        x.AccountId = accountId;
        x.RepositoryIds = repoParam;
        x.Metadata = metadata.SerializeObject();
      });
    }

    public Task<ChangeSummary> SetUserOrganizations(long userId, IEnumerable<long> organizationIds) {
      var orgTable = CreateItemListTable("OrganizationIds", organizationIds);

      return ExecuteAndReadChanges("[dbo].[SetUserOrganizations]", x => {
        x.UserId = userId;
        x.OrganizationIds = orgTable;
      });
    }

    public Task<ChangeSummary> SetOrganizationUsers(long organizationId, IEnumerable<long> userIds) {
      return ExecuteAndReadChanges("[dbo].[SetOrganizationUsers]", x => {
        x.OrganizationId = organizationId;
        x.UserIds = CreateItemListTable("UserIds", userIds);
      });
    }

    public Task<ChangeSummary> SetRepositoryAssignableAccounts(long repositoryId, IEnumerable<long> assignableAccountIds) {
      return ExecuteAndReadChanges("[dbo].[SetRepositoryAssignableAccounts]", x => {
        x.RepositoryId = repositoryId;
        x.AssignableAccountIds = CreateItemListTable("AssignableAccountIds", assignableAccountIds);
      });
    }

    private static SqlParameter CreateItemListTable<T>(string parameterName, IEnumerable<T> values) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[ItemListTableType]",
        new[] { Tuple.Create("Item", typeof(T)) },
        x => new object[] { x },
        values);
    }

    private static SqlParameter CreateCommentTable(string parameterName, IEnumerable<CommentTableType> comments) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[CommentTableType]",
        new[] {
          Tuple.Create("Id", typeof(long)),
          Tuple.Create("IssueNumber", typeof(int)),
          Tuple.Create("UserId", typeof(long)),
          Tuple.Create("Body", typeof(string)),
          Tuple.Create("CreatedAt", typeof(DateTimeOffset)),
          Tuple.Create("UpdatedAt", typeof(DateTimeOffset)),
        },
        x => new object[] {
          x.Id,
          x.IssueNumber,
          x.UserId,
          x.Body,
          x.CreatedAt,
          x.UpdatedAt,
        },
        comments);
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
          x.ItemId,
          x.Color,
          x.Name,
        },
        labels);
    }

    private static SqlParameter CreateVersionTableType(string parameterName, IEnumerable<VersionTableType> versions) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[VersionTableType]",
        new[] {
          Tuple.Create("ItemId", typeof(long)),
          Tuple.Create("RowVersion", typeof(long)),
        },
        x => new object[] {
          x.ItemId,
          x.RowVersion,
        },
        versions);
    }

    private static SqlParameter CreateTableParameter<T>(string parameterName, string typeName, IEnumerable<Tuple<string, Type>> columns, Func<T, object[]> rowValues, IEnumerable<T> rows) {
      if (!typeName.Contains("[")) {
        typeName = $"[dbo].[{typeName}]";
      }

      DataTable table = null;

      try {
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
      } catch {
        if (table != null) {
          table.Dispose();
          table = null;
        }
        throw;
      }
    }
  }
}
