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

  public partial class ShipHubContext : DbContext {
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
    public virtual DbSet<Comment> Comments { get; set; }
    public virtual DbSet<Event> Events { get; set; }
    public virtual DbSet<GitHubMetaData> GitHubMetaData { get; set; }
    public virtual DbSet<Issue> Issues { get; set; }
    public virtual DbSet<Label> Labels { get; set; }
    public virtual DbSet<Milestone> Milestones { get; set; }
    public virtual DbSet<Repository> Repositories { get; set; }

    public virtual IQueryable<User> Users { get { return Accounts.OfType<User>(); } }
    public virtual IQueryable<Organization> Organizations { get { return Accounts.OfType<Organization>(); } }

    public override int SaveChanges() {
      throw new NotImplementedException("Please use SaveChangesAsync instead.");
    }

    protected override void OnModelCreating(DbModelBuilder mb) {
      mb.Entity<AccessToken>()
        .HasMany(e => e.MetaData)
        .WithOptional(e => e.AccessToken)
        .WillCascadeOnDelete();

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
        .HasMany(e => e.OwnedRepositories)
        .WithRequired(e => e.Account)
        .HasForeignKey(e => e.AccountId)
        .WillCascadeOnDelete(false);

      mb.Entity<Account>()
        .HasMany(e => e.LinkedRepositories)
        .WithRequired(e => e.Account)
        .HasForeignKey(e => e.AccountId);

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

      mb.Entity<Organization>()
        .HasMany(e => e.Members)
        .WithMany(e => e.Organizations)
        .Map(m => m.ToTable("AccountOrganizations").MapLeftKey("OrganizationId").MapRightKey("UserId"));

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
        .HasMany(e => e.LinkedAccounts)
        .WithRequired(e => e.Repository)
        .HasForeignKey(e => e.RepositoryId);

      mb.Entity<Repository>()
        .HasMany(e => e.AssignableAccounts)
        .WithMany(e => e.AssignableRepositories)
        .Map(m => m.ToTable("RepositoryAccounts").MapLeftKey("RepositoryId").MapRightKey("AccountId"));
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

    public async Task<bool> SetAccountLinkedRepositories(int accountId, IEnumerable<int> repositoryIds) {
      var result = new SqlParameter("Result", SqlDbType.Int) {
        Direction = ParameterDirection.Output
      };

      await Database.ExecuteSqlCommandAsync(
        @"EXEC @Result = [dbo].[SetAccountLinkedRepositories]
          @AccountId = @AccountId,
          @RepositoryIds = @RepositoryIds;",
        result,
        new SqlParameter("AccountId", SqlDbType.Int) { Value = accountId },
        CreateListTable("RepositoryIds", "IntListTableType", repositoryIds));

      return ((int)result.Value) != 0;
    }

    public async Task<bool> SetRepositoryAssignableAccounts(int repositoryId, IEnumerable<int> assignableAccountIds) {
      var result = new SqlParameter("Result", SqlDbType.Int) {
        Direction = ParameterDirection.Output
      };

      await Database.ExecuteSqlCommandAsync(
        @"EXEC @Result = [dbo].[SetRepositoryAssignableAccounts]
          @RepositoryId = @RepositoryId,
          @AssignableAccountIds = @AssignableAccountIds;",
        result,
        new SqlParameter("RepositoryId", SqlDbType.Int) { Value = repositoryId },
        CreateListTable("AssignableAccountIds", "IntListTableType", assignableAccountIds));

      return ((int)result.Value) != 0;
    }

    public async Task<bool> SetUserOrganizations(int userId, IEnumerable<int> organizationIds) {
      var result = new SqlParameter("Result", SqlDbType.Int) {
        Direction = ParameterDirection.Output
      };

      await Database.ExecuteSqlCommandAsync(
        @"EXEC @Result = [dbo].[SetUserOrganizations]
          @UserId = @UserId,
          @OrganizationIds = @OrganizationIds;",
        result,
        new SqlParameter("UserId", SqlDbType.Int) { Value = userId },
        CreateListTable("OrganizationIds", "IntListTableType", organizationIds));

      return ((int)result.Value) != 0;
    }

    public async Task<bool> SetOrganizationUsers(int organizationId, IEnumerable<int> userIds) {
      var result = new SqlParameter("Result", SqlDbType.Int) {
        Direction = ParameterDirection.Output
      };

      await Database.ExecuteSqlCommandAsync(
        @"EXEC @Result = [dbo].[SetOrganizationUsers]
          @OrganizationId = @OrganizationId,
          @UserIds = @UserIds;",
        result,
        new SqlParameter("OrganizationId", SqlDbType.Int) { Value = organizationId },
        CreateListTable("UserIds", "IntListTableType", userIds));

      return ((int)result.Value) != 0;
    }

    public async Task BulkUpdateAccounts(DateTimeOffset date, IEnumerable<AccountTableType> accounts) {
      var tableParam = CreateTableParameter(
        "Accounts",
        "[dbo].[AccountTableType]",
        new[] {
            Tuple.Create("Id", typeof(int)),
            Tuple.Create("Type", typeof(string)),
            Tuple.Create("AvatarURL", typeof(string)),
            Tuple.Create("Login", typeof(string)),
        },
        x => new object[] {
          x.Id,
          x.Type,
          x.AvatarUrl,
          x.Login,
        },
        accounts);

      await Database.ExecuteSqlCommandAsync(
        "EXEC [dbo].[BulkUpdateAccounts] @Date = @Date, @Accounts = @Accounts;",
        new SqlParameter("Date", SqlDbType.DateTimeOffset) { Value = date },
        tableParam);
    }

    public async Task BulkUpdateRepositories(DateTimeOffset date, IEnumerable<RepositoryTableType> repositories) {
      var tableParam = CreateTableParameter(
        "Repositories",
        "[dbo].[RepositoryTableType]",
        new[] {
            Tuple.Create("Id", typeof(int)),
            Tuple.Create("AccountId", typeof(int)),
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
        "EXEC [dbo].[BulkUpdateRepositories] @Date = @Date, @Repositories = @Repositories;",
        new SqlParameter("Date", SqlDbType.DateTimeOffset) { Value = date },
        tableParam);
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

    private static SqlParameter CreateListTable<T>(string parameterName, string typeName, IEnumerable<T> values) {
      return CreateTableParameter(
        parameterName,
        typeName,
        new[] { Tuple.Create("Item", typeof(T)) },
        x => new object[] { x },
        values);
    }
  }
}
