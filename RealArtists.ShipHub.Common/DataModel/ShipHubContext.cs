namespace RealArtists.ShipHub.Common.DataModel {
  using System;
  using System.Collections.Generic;
  using System.Data;
  using System.Data.Common;
  using System.Data.Entity;
  using System.Data.SqlClient;
  using System.Diagnostics;
  using System.Diagnostics.CodeAnalysis;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;
  using GitHub;
  using Legacy;
  using Newtonsoft.Json;
  using Types;

  [DbConfigurationType(typeof(ShipHubContextConfiguration))]
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
    public virtual DbSet<AccountSettings> AccountSettings { get; set; }
    public virtual DbSet<ApplicationSetting> ApplicationSettings { get; set; }
    public virtual DbSet<AccountSyncRepository> AccountSyncRepositories { get; set; }
    public virtual DbSet<CommitComment> CommitComments { get; set; }
    public virtual DbSet<GitHubToken> Tokens { get; set; }
    public virtual DbSet<Hook> Hooks { get; set; }
    public virtual DbSet<IssueComment> IssueComments { get; set; }
    public virtual DbSet<IssueEvent> IssueEvents { get; set; }
    public virtual DbSet<Issue> Issues { get; set; }
    public virtual DbSet<Label> Labels { get; set; }
    public virtual DbSet<Milestone> Milestones { get; set; }
    public virtual DbSet<Project> Projects { get; set; }
    public virtual DbSet<OrganizationAccount> OrganizationAccounts { get; set; }
    public virtual DbSet<PullRequest> PullRequests { get; set; }
    public virtual DbSet<PullRequestComment> PullRequestComments { get; set; }
    public virtual DbSet<Repository> Repositories { get; set; }
    public virtual DbSet<ProtectedBranch> ProtectedBranches { get; set; }
    public virtual DbSet<Subscription> Subscriptions { get; set; }
    public virtual DbSet<SyncLog> SyncLogs { get; set; }
    public virtual DbSet<Query> Queries { get; set; }
    public virtual DbSet<Usage> Usage { get; set; }

    public virtual IQueryable<User> Users => Accounts.OfType<User>();
    public virtual IQueryable<Organization> Organizations => Accounts.OfType<Organization>();

    public SqlConnectionFactory ConnectionFactory { get; }

    public override int SaveChanges() {
      throw new NotImplementedException("Please use asynchronous methods instead.");
    }

    public override Task<int> SaveChangesAsync() {
      if (Environment.StackTrace.Contains("RealArtists.ShipHub.Api.Tests")) {
        // The current implementation of EF calls SaveChangesAsync(CancellationToken cancellationToken) here,
        // so we could just have the override below. However, in case that changes, keep the test in both
        // places. It should only impact tests.
        return base.SaveChangesAsync();
      } else {
        throw new InvalidOperationException("EF sucks at concurrency. Use a stored procedure instead.");
      }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken) {
      if (Environment.StackTrace.Contains("RealArtists.ShipHub.Api.Tests")) {
        return base.SaveChangesAsync(cancellationToken);
      } else {
        throw new InvalidOperationException("EF sucks at concurrency. Use a stored procedure instead.");
      }
    }

    [SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "instance", Justification = "See comment.")]
    protected override void OnModelCreating(DbModelBuilder modelBuilder) {
      // This gross hack ensure the right DLL gets copied as a dependency of our project.
      // If you must know: http://stackoverflow.com/a/23329890
      var instance = System.Data.Entity.SqlServer.SqlProviderServices.Instance;
      // End Hack

      modelBuilder.Entity<Account>()
        .Map<User>(m => m.Requires("Type").HasValue(Account.UserType))
        .Map<Organization>(m => m.Requires("Type").HasValue(Account.OrganizationType));

      modelBuilder.Entity<Account>()
        .HasMany(e => e.Issues)
        .WithRequired(e => e.User)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
        .HasMany(e => e.OwnedRepositories)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
        .HasOptional(e => e.Subscription)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Account>()
        .HasOptional(e => e.Settings)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Issue>()
        .HasMany(e => e.Assignees)
        .WithMany(e => e.AssignedIssues)
        .Map(m => m.ToTable("IssueAssignees").MapLeftKey("IssueId").MapRightKey("UserId"));

      modelBuilder.Entity<Issue>()
        .HasMany(e => e.Comments)
        .WithRequired(e => e.Issue)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Issue>()
        .HasMany(e => e.Labels)
        .WithMany(e => e.Issues)
        .Map(m => m.ToTable("IssueLabels").MapLeftKey("IssueId").MapRightKey("LabelId"));

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

      modelBuilder.Entity<Repository>()
        .HasMany(e => e.Projects)
        .WithOptional(e => e.Repository)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Repository>()
        .HasMany(e => e.Labels)
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
        .HasMany(e => e.Tokens)
        .WithRequired(e => e.User)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<User>()
        .HasMany(e => e.SyncRepositories)
        .WithRequired(e => e.Account)
        .WillCascadeOnDelete(false);

      modelBuilder.Entity<Organization>()
        .HasMany(e => e.Projects)
        .WithOptional(e => e.Organization)
        .WillCascadeOnDelete(false);
    }

    public Task BumpRepositoryVersion(long repositoryId) {
      return ExecuteCommandTextAsync(
        "UPDATE SyncLog SET [RowVersion] = DEFAULT WHERE OwnerType = 'repo' AND OwnerId = @RepoId AND ItemType = 'repository' and ItemId = @RepoId",
        new SqlParameter("RepoId", SqlDbType.BigInt) { Value = repositoryId });
    }

    public Task BumpOrganizationVersion(long organizationId) {
      return ExecuteCommandTextAsync(
        "UPDATE SyncLog SET [RowVersion] = DEFAULT WHERE OwnerType = 'org' AND OwnerId = @OrgId AND ItemType = 'account' AND ItemId = @OrgId",
        new SqlParameter("OrgId", SqlDbType.BigInt) { Value = organizationId });
    }

    public Task<ChangeSummary> RevokeAccessTokens(long userId) {
      return ExecuteAndReadChanges("[dbo].[RevokeAccessTokens]", x => { x.UserId = userId; });
    }

    public Task UpdateMetadata(string table, long id, GitHubResponse response) {
      return UpdateMetadata(table, "MetadataJson", id, response);
    }

    public Task UpdateMetadata(string table, string metadataColumn, long id, GitHubResponse response) {
      return UpdateMetadata(table, "Id", metadataColumn, id, GitHubMetadata.FromResponse(response));
    }

    public Task UpdateMetadata(string table, long id, GitHubMetadata metadata) {
      return UpdateMetadata(table, "Id", "MetadataJson", id, metadata);
    }

    public Task UpdateMetadata(string table, string metadataColumn, long id, GitHubMetadata metadata) {
      return UpdateMetadata(table, "Id", metadataColumn, id, metadata);
    }

    public Task UpdateMetadata(string table, string keyColumn, string metadataColumn, long key, GitHubMetadata metadata) {
      // This can happen sometimes and doesn't make sense to handle until here.
      // Obviously, don't update.
      if (metadata == null) {
        return Task.CompletedTask;
      }

      return ExecuteCommandTextAsync(
        $@"UPDATE [{table}] SET
             [{metadataColumn}] = @Metadata
           WHERE [{keyColumn}] = @Key
             AND ([{metadataColumn}] IS NULL OR CAST(JSON_VALUE([{metadataColumn}], '$.lastRefresh') as DATETIMEOFFSET) < CAST(JSON_VALUE(@Metadata, '$.lastRefresh') as DATETIMEOFFSET))",
        new SqlParameter("Key", SqlDbType.BigInt) { Value = key },
        new SqlParameter("Metadata", SqlDbType.NVarChar) { Value = metadata.SerializeObject() });
    }

    public Task UpdateRateLimit(GitHubRateLimit limit) {
      return RetryOnDeadlock(async () => {
        using (var sp = new DynamicStoredProcedure("[dbo].[UpdateRateLimit]", ConnectionFactory)) {
          dynamic dsp = sp;
          dsp.Token = limit.AccessToken;
          dsp.RateLimit = limit.Limit;
          dsp.RateLimitRemaining = limit.Remaining;
          dsp.RateLimitReset = limit.Reset;
          return await sp.ExecuteNonQueryAsync();
        }
      });
    }

    public Task SetUserAccessToken(long userId, string scopes, GitHubRateLimit limit) {
      return RetryOnDeadlock(async () => {
        using (var sp = new DynamicStoredProcedure("[dbo].[SetUserAccessToken]", ConnectionFactory)) {
          dynamic dsp = sp;
          dsp.UserId = userId;
          dsp.Scopes = scopes;
          dsp.Token = limit.AccessToken;
          dsp.RateLimit = limit.Limit;
          dsp.RateLimitRemaining = limit.Remaining;
          dsp.RateLimitReset = limit.Reset;
          return await sp.ExecuteNonQueryAsync();
        }
      });
    }

    public Task RollUserAccessToken(long userId, string oldToken, string newToken, int version) {
      return RetryOnDeadlock(async () => {
        using (var sp = new DynamicStoredProcedure("[dbo].[RollToken]", ConnectionFactory)) {
          dynamic dsp = sp;
          dsp.UserId = userId;
          dsp.OldToken = oldToken;
          dsp.NewToken = newToken;
          dsp.Version = version;
          return await sp.ExecuteNonQueryAsync();
        }
      });
    }

    public Task DeleteUserAccessToken(long userId, string token) {
      return ExecuteCommandTextAsync(
        $"DELETE FROM GitHubTokens WHERE Token = @Token AND UserId = @UserId",
        new SqlParameter("Token", SqlDbType.NVarChar, 64) { Value = token },
        new SqlParameter("UserId", SqlDbType.BigInt) { Value = userId });
    }

    public Task UpdateAccountMentionSince(long accountId, DateTimeOffset? mentionSince) {
      return UpdateSince("Accounts", "MentionSince", accountId, mentionSince);
    }

    public Task UpdateRepositoryCommentSince(long repoId, DateTimeOffset? commentSince) {
      return UpdateSince("Repositories", "CommentSince", repoId, commentSince);
    }

    public Task UpdateRepositoryIssueSince(long repoId, DateTimeOffset? issueSince) {
      return UpdateSince("Repositories", "IssueSince", repoId, issueSince);
    }

    private Task UpdateSince(string tableName, string columnName, long id, DateTimeOffset? since) {
      return ExecuteCommandTextAsync(
        $"UPDATE {tableName} SET {columnName} = @Since WHERE Id = @Id",
        new SqlParameter("Since", SqlDbType.DateTimeOffset) { Value = since },
        new SqlParameter("Id", SqlDbType.BigInt) { Value = id });
    }

    public Task UpdateRepositoryReviewVersion(long repoId, long? version) {
      return ExecuteCommandTextAsync(
        "UPDATE Repositories SET PullRequestReviewVersion = @Version WHERE Id = @Id",
        new SqlParameter("Version", SqlDbType.BigInt) { Value = version },
        new SqlParameter("Id", SqlDbType.BigInt) { Value = repoId });
    }

    public Task<ChangeSummary> MarkRepositoryIssuesAsFullyImported(long repoId) {
      return ExecuteAndReadChanges("[dbo].[MarkRepositoryIssuesAsFullyImported]", x => {
        x.RepositoryId = repoId;
      });
    }

    /// <summary>
    /// Insert, Update, or Delete a protected branch
    /// </summary>
    /// <param name="repoId"></param>
    /// <param name="branchName"></param>
    /// <param name="branchProtection">Serialized JSON as returned from GitHub's branch protection API</param>
    /// <param name="metadata">Must not be null</param>
    public Task<ChangeSummary> UpdateProtectedBranch(long repoId, string branchName, string branchProtection, GitHubMetadata metadata) {
      if (branchName == null) {
        throw new ArgumentNullException("branchName");
      }
      if (branchProtection == null) {
        throw new ArgumentNullException("branchProtection");
      }
      if (metadata == null) {
        throw new ArgumentNullException("metadata");
      }
      return ExecuteAndReadChanges("[dbo].[UpdateProtectedBranch]", x => {
        x.RepositoryId = repoId;
        x.Name = branchName;
        x.Protection = branchProtection;
        x.MetadataJson = metadata.SerializeObject();
      });
    }

    public Task<ChangeSummary> DeleteProtectedBranch(long repoId, string branchName) {
      if (branchName == null) {
        throw new ArgumentNullException("branchName");
      }
      return ExecuteAndReadChanges("[dbo].[DeleteProtectedBranch]", x => {
        x.RepositoryId = repoId;
        x.Name = branchName;
      });
    }

    private Task<int> ExecuteCommandTextAsync(string commandText, params SqlParameter[] parameters) {
      return RetryOnDeadlock(async () => {
        using (var conn = ConnectionFactory.Get())
        using (var cmd = new SqlCommand(commandText, conn)) {
          try {
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddRange(parameters);
            if (conn.State != ConnectionState.Open) {
              await conn.OpenAsync();
            }
            return await cmd.ExecuteNonQueryAsync();
          } finally {
            if (conn.State != ConnectionState.Closed) {
              conn.Close();
            }
          }
        }
      });
    }

    private async Task<TResult> RetryOnDeadlock<TResult>(Func<Task<TResult>> query, int maxAttempts = 2) {
      for (var attempt = 1; ; ++attempt) {
        try {
          return await query();
        } catch (SqlException ex) {
          if (ex.Number == 1205 && attempt < maxAttempts) {
            // Retry deadlock
            continue;
          }
          throw;
        }
      }
    }

    private Task<ChangeSummary> ExecuteAndReadChanges(string procedureName, Action<dynamic> applyParams) {
      return RetryOnDeadlock(async () => {
        var result = new ChangeSummary();

        using (var dsp = new DynamicStoredProcedure(procedureName, ConnectionFactory)) {
          applyParams(dsp);

          using (var sdr = await dsp.ExecuteReaderAsync()) {
            dynamic ddr = sdr;
            do {
              while (sdr.Read()) {
                long itemId = ddr.ItemId;
                switch ((string)ddr.ItemType) {
                  case "org":
                    result.Organizations.Add(itemId);
                    break;
                  case "repo":
                    result.Repositories.Add(itemId);
                    break;
                  case "user":
                  case "account": // I almost used this today accidentally.
                    result.Users.Add(itemId);
                    break;
                  default:
                    throw new Exception($"Unknown change ItemType {ddr.ItemType}");
                }
              }
            } while (sdr.NextResult());

            return result;
          }
        }
      });
    }

    /// <summary>
    /// Computes the repository sync list for a user from their sync settings.
    /// </summary>
    /// <param name="accountId">The user's id.</param>
    /// <param name="autoTrack">True to automatically add newly discovered repos.</param>
    /// <param name="include">Repos to explicitly include.</param>
    /// <param name="exclude">Repos to explicitly exclude.</param>
    /// <returns>The computed list of repositories to sync.</returns>
    public Task<IDictionary<long, GitHubMetadata>> UpdateAccountSyncRepositories(long accountId, bool autoTrack, IEnumerable<StringMappingTableType> include, IEnumerable<long> exclude) {
      return RetryOnDeadlock(async () => {
        IDictionary<long, GitHubMetadata> result = new Dictionary<long, GitHubMetadata>();

        using (var dsp = new DynamicStoredProcedure("[dbo].[UpdateAccountSyncRepositories]", ConnectionFactory)) {
          dynamic x = dsp;
          x.AccountId = accountId;
          x.AutoTrack = autoTrack;
          if (exclude?.Any() == true) {
            x.Exclude = CreateItemListTable("Exclude", exclude);
          }
          if (include?.Any() == true) {
            x.Include = CreateTableParameter(
              "Include",
              "[dbo].[StringMappingTableType]",
              new[] {
                ("Key", typeof(long)),
                ("Value", typeof(string)),
              },
              y => new object[] {
                y.Key,
                y.Value,
              },
              include);
          }

          using (var sdr = await dsp.ExecuteReaderAsync()) {
            dynamic ddr = sdr;

            while (sdr.Read()) {
              var repoId = (long)ddr.RepositoryId;
              var metadata = ((string)ddr.RepoMetadataJson).DeserializeObject<GitHubMetadata>();
              result.Add(repoId, metadata);
            }
          }
        }

        return result;
      });
    }

    public Task<ChangeSummary> UpdateAccount(DateTimeOffset date, AccountTableType account) {
      return BulkUpdateAccounts(date, new[] { account });
    }

    public Task<ChangeSummary> BulkUpdateAccounts(DateTimeOffset date, IEnumerable<AccountTableType> accounts) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateAccounts]", x => {
        x.Date = date;
        x.Accounts = CreateTableParameter(
          "Accounts",
          "[dbo].[AccountTableType]",
          new[] {
            ("Id", typeof(long)),
            ("Type", typeof(string)),
            ("Login", typeof(string)),
            ("Name", typeof(string)),
            ("Email", typeof(string)),
          },
          y => new object[] {
            y.Id,
            y.Type,
            y.Login,
            y.Name,
            y.Email,
          },
          accounts);
      });
    }

    public Task<ChangeSummary> BulkUpdateCommitComments(long repositoryId, IEnumerable<CommitCommentTableType> comments) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateCommitComments]", x => {
        x.RepositoryId = repositoryId;
        x.Comments = CreateTableParameter(
          "Comments",
          "[dbo].[CommitCommentTableType]",
          new[] {
            ("Id", typeof(long)),
            ("UserId", typeof(long)),
            ("CommitId", typeof(string)),
            ("Path", typeof(string)),
            ("Line", typeof(long)),
            ("Position", typeof(long)),
            ("Body", typeof(string)),
            ("CreatedAt", typeof(DateTimeOffset)),
            ("UpdatedAt", typeof(DateTimeOffset)),
          },
          y => new object[] {
            y.Id,
            y.UserId,
            y.CommitId,
            y.Path,
            y.Line,
            y.Position,
            y.Body,
            y.CreatedAt,
            y.UpdatedAt,
          },
          comments);
      });
    }

    public Task<ChangeSummary> BulkUpdateCommitStatuses(long repositoryId, string reference, IEnumerable<CommitStatusTableType> statuses) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateCommitStatuses]", x => {
        x.RepositoryId = repositoryId;
        x.Reference = reference;
        x.Statuses = CreateTableParameter(
          "Statuses",
          "[dbo].[CommitStatusTableType]",
          new[] {
            ("Id", typeof(long)),
            ("State", typeof(string)),
            ("TargetUrl", typeof(string)),
            ("Description", typeof(string)),
            ("Context", typeof(string)),
            ("CreatedAt", typeof(DateTimeOffset)),
            ("UpdatedAt", typeof(DateTimeOffset)),
          },
          y => new object[] {
            y.Id,
            y.State,
            y.TargetUrl,
            y.Description,
            y.Context,
            y.CreatedAt,
            y.UpdatedAt,
          },
          statuses);
      });
    }

    public Task<ChangeSummary> BulkUpdateIssueComments(long repositoryId, IEnumerable<CommentTableType> comments) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateComments]", x => {
        x.RepositoryId = repositoryId;
        x.Comments = CreateCommentTable("Comments", comments);
      });
    }

    public Task<ChangeSummary> BulkUpdateIssueEvents(
      long userId,
      long repositoryId,
      IEnumerable<IssueEventTableType> issueEvents,
      IEnumerable<long> referencedAccounts) {
      return BulkUpdateEvents(userId, repositoryId, false, issueEvents, referencedAccounts);
    }

    public Task<ChangeSummary> BulkUpdateTimelineEvents(
      long userId,
      long repositoryId,
      IEnumerable<IssueEventTableType> issueEvents,
      IEnumerable<long> referencedAccounts) {
      return BulkUpdateEvents(userId, repositoryId, true, issueEvents, referencedAccounts);
    }

    private Task<ChangeSummary> BulkUpdateEvents(
      long userId,
      long repositoryId,
      bool fromTimeline,
      IEnumerable<IssueEventTableType> issueEvents,
      IEnumerable<long> referencedAccounts) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateIssueEvents]", x => {
        x.UserId = userId;
        x.RepositoryId = repositoryId;
        x.Timeline = fromTimeline;
        x.ReferencedAccounts = CreateItemListTable("ReferencedAccounts", referencedAccounts);
        x.IssueEvents = CreateTableParameter(
          "IssueEvents",
          "[dbo].[IssueEventTableType]",
          new[] {
            ("UniqueKey", typeof(string)),
            ("Id", typeof(long)),
            ("IssueId", typeof(long)),
            ("ActorId", typeof(long)),
            ("Event", typeof(string)),
            ("CreatedAt", typeof(DateTimeOffset)),
            ("Hash", typeof(Guid)),
            ("Restricted", typeof(bool)),
            ("ExtensionData", typeof(string)),
          },
          y => new object[] {
            y.UniqueKey,
            y.Id,
            y.IssueId,
            y.ActorId,
            y.Event,
            y.CreatedAt,
            y.Hash,
            y.Restricted,
            y.ExtensionData,
          },
          issueEvents);
      });
    }

    public Task<ChangeSummary> BulkUpdateIssues(
      long repositoryId,
      IEnumerable<IssueTableType> issues,
      IEnumerable<IssueMappingTableType> labels,
      IEnumerable<IssueMappingTableType> assignees) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateIssues]", x => {
        x.RepositoryId = repositoryId;
        x.Issues = CreateTableParameter(
          "Issues",
          "[dbo].[IssueTableType]",
          new[] {
            ("Id", typeof(long)),
            ("UserId", typeof(long)),
            ("Number", typeof(int)),
            ("State", typeof(string)),
            ("Title", typeof(string)),
            ("Body", typeof(string)),
            ("MilestoneId", typeof(long)),
            ("Locked", typeof(bool)),
            ("CreatedAt", typeof(DateTimeOffset)),
            ("UpdatedAt", typeof(DateTimeOffset)),
            ("ClosedAt", typeof(DateTimeOffset)),
            ("ClosedById", typeof(long)),
            ("PullRequest", typeof(bool)),
            ("Reactions", typeof(string)),
          },
          y => new object[] {
            y.Id,
            y.UserId,
            y.Number,
            y.State,
            y.Title,
            y.Body,
            y.MilestoneId,
            y.Locked,
            y.CreatedAt,
            y.UpdatedAt,
            y.ClosedAt,
            y.ClosedById,
            y.PullRequest,
            y.Reactions,
          },
          issues);

        if (labels != null) {
          x.Labels = CreateIssueMappingTable("Labels", labels);
        }

        if (assignees != null) {
          x.Assignees = CreateIssueMappingTable("Assignees", assignees);
        }
      });
    }

    public Task<ChangeSummary> BulkUpdateIssueMentions(
      long userId,
      IEnumerable<long> issues,
      bool complete = false) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateIssueMentions]", x => {
        x.UserId = userId;
        if (issues?.Any() == true) {
          x.Issues = CreateItemListTable("Issues", issues);
        }
        x.Complete = complete;
      });
    }

    public Task<ChangeSummary> BulkUpdatePullRequests(
      long repositoryId,
      IEnumerable<PullRequestTableType> pullRequests,
      IEnumerable<IssueMappingTableType> reviewers) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdatePullRequests]", x => {
        x.RepositoryId = repositoryId;
        x.PullRequests = CreateTableParameter(
          "PullRequests",
          "[dbo].[PullRequestTableType]",
          new[] {
            ("Id", typeof(long)),
            ("Number", typeof(int)),
            ("IssueId", typeof(long)),
            ("CreatedAt", typeof(DateTimeOffset)),
            ("UpdatedAt", typeof(DateTimeOffset)),
            ("MergeCommitSha", typeof(string)),
            ("MergedAt", typeof(DateTimeOffset)),
            ("BaseJson", typeof(string)),
            ("HeadJson", typeof(string)),
            ("Additions", typeof(int)),
            ("ChangedFiles", typeof(int)),
            ("Commits", typeof(int)),
            ("Deletions", typeof(int)),
            ("MaintainerCanModify", typeof(bool)),
            ("Mergeable", typeof(string)),
            ("MergeableState", typeof(string)),
            ("MergedById", typeof(long)),
            ("Rebaseable", typeof(bool)),
            ("Hash", typeof(Guid)),
          },
          y => new object[] {
            y.Id,
            y.Number,
            y.IssueId,
            y.CreatedAt,
            y.UpdatedAt,
            y.MergeCommitSha,
            y.MergedAt,
            y.BaseJson,
            y.HeadJson,
            y.Additions,
            y.ChangedFiles,
            y.Commits,
            y.Deletions,
            y.MaintainerCanModify,
            y.Mergeable,
            y.MergeableState,
            y.MergedById,
            y.Rebaseable,
            y.Hash,
          },
          pullRequests);

        if (reviewers != null) {
          x.Reviewers = CreateIssueMappingTable("Reviewers", reviewers);
        }
      });
    }

    public Task<ChangeSummary> BulkUpdatePullRequestComments(
      long repositoryId,
      long issueId,
      IEnumerable<PullRequestCommentTableType> comments,
      long? pendingReviewId = null,
      bool dropWithMissingReview = false) {
      if (pendingReviewId != null && comments.Any(x => x.PullRequestReviewId != pendingReviewId)) {
        throw new InvalidOperationException($"All comments must be for {nameof(pendingReviewId)} if specified.");
      }

      return ExecuteAndReadChanges("[dbo].[BulkUpdatePullRequestComments]", x => {
        x.RepositoryId = repositoryId;
        x.IssueId = issueId;
        x.PendingReviewId = pendingReviewId;
        x.DropWithMissingReview = dropWithMissingReview;
        x.Comments = CreateTableParameter(
          "Comments",
          "[dbo].[PullRequestCommentTableType]",
          new[] {
            ("Id", typeof(long)),
            ("UserId", typeof(long)),
            ("PullRequestReviewId", typeof(long)),
            ("DiffHunk", typeof(string)),
            ("Path", typeof(string)),
            ("Position", typeof(long)),
            ("OriginalPosition", typeof(long)),
            ("CommitId", typeof(string)),
            ("OriginalCommitId", typeof(string)),
            ("Body", typeof(string)),
            ("CreatedAt", typeof(DateTimeOffset)),
            ("UpdatedAt", typeof(DateTimeOffset)),
          },
          y => new object[] {
            y.Id,
            y.UserId,
            y.PullRequestReviewId,
            y.DiffHunk,
            y.Path,
            y.Position,
            y.OriginalPosition,
            y.CommitId,
            y.OriginalCommitId,
            y.Body,
            y.CreatedAt,
            y.UpdatedAt,
          },
          comments);
      });
    }

    public Task<ChangeSummary> BulkUpdateMilestones(long repositoryId, IEnumerable<MilestoneTableType> milestones, bool complete = false) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateMilestones]", x => {
        x.RepositoryId = repositoryId;
        x.Complete = complete;
        x.Milestones = CreateTableParameter(
          "Milestones",
          "[dbo].[MilestoneTableType]",
          new[] {
            ("Id", typeof(long)),
            ("Number", typeof(int)),
            ("State", typeof(string)),
            ("Title", typeof(string)),
            ("Description", typeof(string)),
            ("CreatedAt", typeof(DateTimeOffset)),
            ("UpdatedAt", typeof(DateTimeOffset)),
            ("ClosedAt", typeof(DateTimeOffset)), // Nullable types handled by DataTable
            ("DueOn", typeof(DateTimeOffset)), // Nullable types handled by DataTable
          },
          y => new object[] {
            y.Id,
            y.Number,
            y.State,
            y.Title,
            y.Description,
            y.CreatedAt,
            y.UpdatedAt,
            y.ClosedAt,
            y.DueOn,
          },
          milestones);
      });
    }

    private Task<ChangeSummary> BulkUpdateProjects(IEnumerable<ProjectTableType> projects, long? repositoryId = null, long? organizationId = null) {
      Debug.Assert(repositoryId != null || organizationId != null, "Must specify either repositoryId or organizationId");
      Debug.Assert(!(repositoryId != null && organizationId != null), "Must specify exactly one of repositoryId or organizationId");

      return ExecuteAndReadChanges("[dbo].[BulkUpdateProjects]", x => {
        x.RepositoryId = repositoryId;
        x.OrganizationId = organizationId;
        x.Projects = CreateTableParameter(
          "Projects",
          "[dbo].[ProjectTableType]",
          new[] {
            ("Id", typeof(long)),
            ("Name", typeof(string)),
            ("Number", typeof(long)),
            ("Body", typeof(string)),
            ("CreatedAt", typeof(DateTimeOffset)),
            ("UpdatedAt", typeof(DateTimeOffset)),
            ("CreatorId", typeof(long)),
          },
          y => new object[] {
            y.Id,
            y.Name,
            y.Number,
            y.Body,
            y.CreatedAt,
            y.UpdatedAt,
            y.CreatorId,
          },
          projects);
      });
    }

    public Task<ChangeSummary> BulkUpdateRepositoryProjects(long repositoryId, IEnumerable<ProjectTableType> projects) {
      return BulkUpdateProjects(projects, repositoryId: repositoryId);
    }

    public Task<ChangeSummary> BulkUpdateReviews(long repositoryId, long issueId, DateTimeOffset date, IEnumerable<ReviewTableType> reviews, long? userId = null, bool complete = false) {
      if (userId == null) {
        // Disallow pending reviews
        if (reviews.Any(x => x.State.Equals("pending", StringComparison.OrdinalIgnoreCase))) {
          throw new InvalidOperationException($"Pending reviews require {nameof(userId)} be provided.");
        }
      }

      return ExecuteAndReadChanges("[dbo].[BulkUpdateReviews]", x => {
        x.RepositoryId = repositoryId;
        x.IssueId = issueId;
        x.Date = date;
        x.UserId = userId;
        x.Complete = complete;
        x.Reviews = CreateTableParameter(
          "Reviews",
          "[dbo].[ReviewTableType]",
          new[] {
            ("Id", typeof(long)),
            ("UserId", typeof(long)),
            ("Body", typeof(string)),
            ("CommitId", typeof(string)),
            ("State", typeof(string)),
            ("SubmittedAt", typeof(DateTimeOffset)),
            ("Hash", typeof(Guid)),
          },
          y => new object[] {
            y.Id,
            y.UserId,
            y.Body,
            y.CommitId,
            y.State,
            y.SubmittedAt,
            y.Hash,
          },
          reviews);
      });
    }

    public Task<ChangeSummary> BulkUpdateOrganizationProjects(long organizationId, IEnumerable<ProjectTableType> projects) {
      return BulkUpdateProjects(projects, organizationId: organizationId);
    }

    public Task<ChangeSummary> BulkUpdateLabels(long repositoryId, IEnumerable<LabelTableType> labels, bool complete = false) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateLabels]", x => {
        x.RepositoryId = repositoryId;
        x.Complete = complete;
        x.Labels = CreateTableParameter(
          "Labels",
          "[dbo].[LabelTableType]",
          new[] {
            ("Id", typeof(long)),
            ("Color", typeof(string)),
            ("Name", typeof(string)),
          },
          y => new object[] {
            y.Id,
            y.Color,
            y.Name,
          },
          labels);
      });
    }

    public Task<ChangeSummary> BulkUpdateReactions(
      long repositoryId,
      IEnumerable<ReactionTableType> reactions,
      long? issueId = null,
      long? issueCommentId = null,
      long? commitCommentId = null,
      long? pullRequestCommentId = null) {

      return ExecuteAndReadChanges("[dbo].[BulkUpdateReactions]", x => {
        x.RepositoryId = repositoryId;
        x.IssueId = issueId;
        x.IssueCommentId = issueCommentId;
        x.CommitCommentId = commitCommentId;
        x.PullRequestCommentId = pullRequestCommentId;
        x.Reactions = CreateTableParameter(
          "Reactions",
          "[dbo].[ReactionTableType]",
          new[] {
            ("Id", typeof(long)),
            ("UserId", typeof(long)),
            ("Content", typeof(string)),
            ("CreatedAt", typeof(DateTimeOffset)),
          },
          y => new object[] {
            y.Id,
            y.UserId,
            y.Content,
            y.CreatedAt,
          },
          reactions);
      });
    }

    public Task<ChangeSummary> BulkUpdateRepositories(DateTimeOffset date, IEnumerable<RepositoryTableType> repositories) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateRepositories]", x => {
        x.Date = date;
        x.Repositories = CreateTableParameter(
          "Repositories",
          "[dbo].[RepositoryTableType]",
          new[] {
            ("Id", typeof(long)),
            ("AccountId", typeof(long)),
            ("Private", typeof(bool)),
            ("Name", typeof(string)),
            ("FullName", typeof(string)),
            ("Size", typeof(long)),
            ("HasIssues", typeof(bool)),
            ("HasProjects", typeof(bool)),
            ("Disabled", typeof(bool)),
            ("Archived", typeof(bool)),
            ("AllowMergeCommit", typeof(bool)),
            ("AllowRebaseMerge", typeof(bool)),
            ("AllowSquashMerge", typeof(bool)),
          },
          y => new object[] {
            y.Id,
            y.AccountId,
            y.Private,
            y.Name,
            y.FullName,
            y.Size,
            y.HasIssues,
            y.HasProjects,
            y.Disabled,
            y.Archived,
            y.AllowMergeCommit,
            y.AllowRebaseMerge,
            y.AllowSquashMerge,
          },
          repositories);
      });
    }

    /// <summary>
    /// Deletes the comment.
    /// </summary>
    /// <param name="commentId">The id of the comment to delete.</param>
    /// <param name="date">The date at which you suspected the comment was deleted, or null to force deletion if you know for sure it was removed.</param>
    public Task<ChangeSummary> DeleteIssueComment(long commentId, DateTimeOffset? date) {
      return ExecuteAndReadChanges("[dbo].[DeleteIssueComment]", x => {
        x.CommentId = commentId;
        x.Date = date;
      });
    }

    /// <summary>
    /// Deletes the comment.
    /// </summary>
    /// <param name="commentId">The id of the comment to delete.</param>
    /// <param name="date">The date at which you suspected the comment was deleted, or null to force deletion if you know for sure it was removed.</param>
    public Task<ChangeSummary> DeleteCommitComment(long commentId, DateTimeOffset? date) {
      return ExecuteAndReadChanges("[dbo].[DeleteCommitComment]", x => {
        x.CommentId = commentId;
        x.Date = date;
      });
    }

    /// <summary>
    /// Deletes the comment.
    /// </summary>
    /// <param name="commentId">The id of the comment to delete.</param>
    /// <param name="date">The date at which you suspected the comment was deleted, or null to force deletion if you know for sure it was removed.</param>
    public Task<ChangeSummary> DeletePullRequestComment(long commentId, DateTimeOffset? date) {
      return ExecuteAndReadChanges("[dbo].[DeletePullRequestComment]", x => {
        x.CommentId = commentId;
        x.Date = date;
      });
    }

    public Task<ChangeSummary> DeleteMilestone(long milestoneId) {
      return ExecuteAndReadChanges("[dbo].[DeleteMilestone]", x => {
        x.MilestoneId = milestoneId;
      });
    }

    public Task<ChangeSummary> DeleteLabel(long labelId) {
      return ExecuteAndReadChanges("[dbo].[DeleteLabel]", x => {
        x.LabelId = labelId;
      });
    }

    public Task<ChangeSummary> DeleteReaction(long reactionId) {
      return ExecuteAndReadChanges("[dbo].[DeleteReaction]", x => {
        x.ReactionId = reactionId;
      });
    }

    public Task<ChangeSummary> DeleteRepositories(IEnumerable<long> repositories) {
      Log.Info($"Deleting repositories: [{string.Join(",", repositories)}]");
      return ExecuteAndReadChanges("[dbo].[DeleteRepositories]", x => {
        x.Repositories = CreateItemListTable("Repositories", repositories);
      });
    }

    public Task<ChangeSummary> DeleteReview(long reviewId) {
      return ExecuteAndReadChanges("[dbo].[DeleteReview]", x => {
        x.ReviewId = reviewId;
      });
    }

    public Task<ChangeSummary> DeleteReviewers(string repositoryFullName, int pullRequestNumber, IEnumerable<string> reviewers) {
      return ExecuteAndReadChanges("[dbo].[DeleteReviewers]", x => {
        x.RepositoryFullName = repositoryFullName;
        x.PullRequestNumber = pullRequestNumber;
        x.ReviewersJson = reviewers.SerializeObject(Formatting.None);
      });
    }

    public Task<ChangeSummary> ForceResyncRepositoryIssues(long repositoryId) {
      return ExecuteAndReadChanges("[dbo].[ForceResyncRepositoryIssues]", x => {
        x.RepositoryId = repositoryId;
      });
    }

    public Task<ChangeSummary> ToggleWatchQuery(Guid queryId, long watcherId, bool watch) {
      return ExecuteAndReadChanges("[dbo].[WatchQuery]", x => {
        x.Id = queryId;
        x.WatcherId = watcherId;
        x.Watch = watch;
      });
    }

    public Task<ChangeSummary> UpdateQuery(Guid queryId, long authorId, string title, string predicate) {
      return ExecuteAndReadChanges("[dbo].[UpdateQuery]", x => {
        x.Id = queryId;
        x.AuthorId = authorId;
        x.Title = title;
        x.Predicate = predicate;
      });
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "We're returning it for use elsewhere.")]
    public DynamicStoredProcedure PrepareSync(long userId, long pageSize, IEnumerable<VersionTableType> repoVersions, IEnumerable<VersionTableType> orgVersions, bool selectiveSync, long queriesVersion) {
      var sp = new DynamicStoredProcedure("[dbo].[WhatsNew]", ConnectionFactory);
      dynamic dsp = sp;
      dsp.UserId = userId;
      dsp.SelectiveSync = selectiveSync;
      dsp.PageSize = pageSize;
      dsp.RepositoryVersions = CreateVersionTableType("RepositoryVersions", repoVersions);
      dsp.OrganizationVersions = CreateVersionTableType("OrganizationVersions", orgVersions);
      dsp.QueriesVersion = queriesVersion;
      return sp;
    }

    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "We're returning it for use elsewhere.")]
    public DynamicStoredProcedure SyncSpiderProgress(long userId, bool selectiveSync) {
      var sp = new DynamicStoredProcedure("[dbo].[SyncSpiderProgress]", ConnectionFactory);
      dynamic dsp = sp;
      dsp.UserId = userId;
      dsp.SelectiveSync = selectiveSync;
      return sp;
    }


    public Task<ChangeSummary> SetAccountLinkedRepositories(long accountId, IEnumerable<RepositoryPermissionsTableType> permissions) {
      return ExecuteAndReadChanges("[dbo].[SetAccountLinkedRepositories]", x => {
        x.AccountId = accountId;
        x.Permissions = CreateTableParameter(
          "Permissions",
          "[dbo].[RepositoryPermissionsTableType]",
          new[] {
            ("RepositoryId", typeof(long)),
            ("Admin", typeof(bool)),
            ("Push", typeof(bool)),
            ("Pull", typeof(bool)),
          },
          y => new object[] {
            y.RepositoryId,
            y.Admin,
            y.Push,
            y.Pull,
          },
          permissions);
      });
    }

    public Task SetAccountSettings(long accountId, SyncSettings syncSettings) {
      return RetryOnDeadlock(async () => {
        using (var sp = new DynamicStoredProcedure("[dbo].[SetAccountSettings]", ConnectionFactory)) {
          dynamic dsp = sp;
          dsp.AccountId = accountId;
          dsp.SyncSettingsJson = syncSettings.SerializeObject(Formatting.None);
          return await sp.ExecuteNonQueryAsync();
        }
      });
    }

    public Task<ChangeSummary> SetUserOrganizations(long userId, IEnumerable<long> organizationIds) {
      return ExecuteAndReadChanges("[dbo].[SetUserOrganizations]", x => {
        x.UserId = userId;
        x.OrganizationIds = CreateItemListTable("OrganizationIds", organizationIds);
      });
    }

    public Task<ChangeSummary> SetOrganizationAdmins(long organizationId, IEnumerable<long> adminIds) {
      return ExecuteAndReadChanges("[dbo].[SetOrganizationAdmins]", x => {
        x.OrganizationId = organizationId;
        x.AdminIds = CreateItemListTable("AdminIds", adminIds);
      });
    }

    public Task<ChangeSummary> SetRepositoryAssignableAccounts(long repositoryId, IEnumerable<long> assignableAccountIds) {
      return ExecuteAndReadChanges("[dbo].[SetRepositoryAssignableAccounts]", x => {
        x.RepositoryId = repositoryId;
        x.AssignableAccountIds = CreateItemListTable("AssignableAccountIds", assignableAccountIds);
      });
    }

    public Task<ChangeSummary> SetRepositoryIssueTemplate(long repositoryId, string issueTemplate, string pullRequestTemplate) {
      return ExecuteAndReadChanges("[dbo].[SetRepositoryIssueTemplate]", x => {
        x.RepositoryId = repositoryId;
        x.IssueTemplate = issueTemplate;
        x.PullRequestTemplate = pullRequestTemplate;
      });
    }

    public Task RecordUsage(long accountId, DateTimeOffset date) {
      if (date.Offset != TimeSpan.Zero) {
        throw new ArgumentException("date must be in UTC");
      }

      return RetryOnDeadlock(async () => {
        using (var sp = new DynamicStoredProcedure("[dbo].[RecordUsage]", ConnectionFactory)) {
          dynamic dsp = sp;
          dsp.AccountId = accountId;
          dsp.Date = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
          return await sp.ExecuteNonQueryAsync();
        }
      });
    }

    public Task<ChangeSummary> DisableRepository(long repositoryId, bool disabled) {
      return ExecuteAndReadChanges("[dbo].[DisableRepository]", x => {
        x.RepositoryId = repositoryId;
        x.Disabled = disabled;
      });
    }

    public Task SaveRepositoryMetadata(
      long repositoryId,
      long repoSize,
      GitHubMetadata metadata,
      GitHubMetadata assignableMetadata,
      GitHubMetadata issueMetadata,
      DateTimeOffset issueSince,
      GitHubMetadata labelMetadata,
      GitHubMetadata milestoneMetadata,
      GitHubMetadata projectMetadata,
      GitHubMetadata contentsRootMetadata,
      GitHubMetadata contentsDotGitHubMetadata,
      GitHubMetadata contentsIssueTemplateMetadata,
      GitHubMetadata contentsPullRequestTemplateMetadata,
      GitHubMetadata pullRequestMetadata,
      DateTimeOffset? pullRequestUpdatedAt,
      uint pullRequestSkip,
      IDictionary<string, GitHubMetadata> protectedBranchMetadata) {
      return RetryOnDeadlock(async () => {
        using (var sp = new DynamicStoredProcedure("[dbo].[SaveRepositoryMetadata]", ConnectionFactory)) {
          dynamic dsp = sp;
          dsp.RepositoryId = repositoryId;
          dsp.Size = repoSize;
          dsp.MetadataJson = metadata.SerializeObject();
          dsp.AssignableMetadataJson = assignableMetadata.SerializeObject();
          dsp.IssueMetadataJson = issueMetadata.SerializeObject();
          dsp.IssueSince = issueSince;
          dsp.LabelMetadataJson = labelMetadata.SerializeObject();
          dsp.MilestoneMetadataJson = milestoneMetadata.SerializeObject();
          dsp.ProjectMetadataJson = projectMetadata.SerializeObject();
          dsp.ContentsRootMetadataJson = contentsRootMetadata.SerializeObject();
          dsp.ContentsDotGitHubMetadataJson = contentsDotGitHubMetadata.SerializeObject();
          dsp.ContentsIssueTemplateMetadataJson = contentsIssueTemplateMetadata.SerializeObject();
          dsp.ContentsPullRequestTemplateMetadataJson = contentsPullRequestTemplateMetadata.SerializeObject();
          dsp.PullRequestMetadataJson = pullRequestMetadata.SerializeObject();
          dsp.PullRequestUpdatedAt = pullRequestUpdatedAt;
          dsp.PullRequestSkip = (int)pullRequestSkip;
          dsp.ProtectedBranchMetadata = CreateTableParameter(
            "ProtectedBranchMetadata",
            "[dbo].[ProtectedBranchMetadataTableType]",
            new[] {
              ("Name", typeof(string)),
              ("MetadataJson", typeof(string))
            },
            pbr => new object[] {
              pbr.Key,
              pbr.Value.SerializeObject()
            },
            protectedBranchMetadata
          );

          return await sp.ExecuteNonQueryAsync();
        }
      });
    }

    public Task<ChangeSummary> BulkUpdateHooks(
      IEnumerable<HookTableType> hooks = null,
      IEnumerable<long> seen = null,
      IEnumerable<long> deleted = null) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateHooks]", x => {
        if (hooks?.Any() == true) {
          x.Hooks = CreateTableParameter(
            "Hooks",
            "[dbo].[HookTableType]",
            new[] {
              ("Id", typeof(long)),
              ("GitHubId", typeof(long)),
              ("Secret", typeof(Guid)),
              ("Events", typeof(string)),
              ("LastError", typeof(DateTimeOffset)),
            },
            y => new object[] {
              y.Id,
              y.GitHubId,
              y.Secret,
              y.Events,
              y.LastError,
            },
            hooks);
        }

        if (seen?.Any() == true) {
          x.Seen = CreateItemListTable("Seen", seen);
        }

        if (deleted?.Any() == true) {
          x.Deleted = CreateItemListTable("Deleted", deleted);
        }
      });
    }

    public Task<HookTableType> CreateHook(Guid secret, string events, long? organizationId = null, long? repositoryId = null) {
      if ((organizationId == null) == (repositoryId == null)) {
        throw new ArgumentException($"Exactly one of {nameof(organizationId)} and {nameof(repositoryId)} must be non-null.");
      }

      return RetryOnDeadlock(async () => {
        using (var sp = new DynamicStoredProcedure("[dbo].[CreateHook]", ConnectionFactory)) {
          dynamic dsp = sp;
          dsp.Secret = secret;
          dsp.Events = events;
          dsp.OrganizationId = organizationId;
          dsp.RepositoryId = repositoryId;

          using (var sdr = await sp.ExecuteReaderAsync(CommandBehavior.SingleRow)) {
            sdr.Read();
            dynamic ddr = sdr;
            return new HookTableType() {
              Id = ddr.Id,
              Secret = ddr.Secret,
              Events = ddr.Events,
            };
          }
        }
      });
    }

    public async Task<IEnumerable<ExcessHookType>> ExcessHooks() {
      var results = new List<ExcessHookType>();

      using (var sp = new DynamicStoredProcedure("[dbo].[ExcessHooks]", ConnectionFactory)) {
        dynamic dsp = sp;

        using (var sdr = await sp.ExecuteReaderAsync()) {
          dynamic ddr = sdr;
          while (sdr.Read()) {

            results.Add(new ExcessHookType() {
              Id = ddr.Id,
              GitHubId = ddr.GitHubId,
              AccountId = ddr.AccountId,
              RepoFullName = ddr.RepoFullName,
            });
          }
        }
      }

      return results;
    }

    public Task<ChangeSummary> BulkUpdateSubscriptions(IEnumerable<SubscriptionTableType> subscriptions) {
      return ExecuteAndReadChanges("[dbo].[BulkUpdateSubscriptions]", x => {
        x.Subscriptions = CreateTableParameter(
          "Subscriptions",
          "[dbo].[SubscriptionTableType]",
          new[] {
            ("AccountId", typeof(long)),
            ("State", typeof(string)),
            ("TrialEndDate", typeof(DateTimeOffset)),
            ("Version", typeof(long)),
          },
          y => new object[] {
            y.AccountId,
            y.State,
            y.TrialEndDate,
            y.Version,
          },
          subscriptions);
      });
    }

    public async Task<LogoutHookDetails> GetLogoutWebhooks(long userId) {
      return await RetryOnDeadlock(async () => {
        var repos = new List<WebhookDetails>();
        var orgs = new List<WebhookDetails>();

        using (var sp = new DynamicStoredProcedure("[dbo].[LogoutWebhooks]", ConnectionFactory)) {
          dynamic dsp = sp;
          dsp.UserId = userId;

          using (var sdr = await sp.ExecuteReaderAsync()) {
            dynamic ddr = sdr;
            while (sdr.Read()) {
              repos.Add(new WebhookDetails(ddr.HookId, ddr.FullName));
            }
            sdr.NextResult();
            while (sdr.Read()) {
              orgs.Add(new WebhookDetails(ddr.HookId, ddr.Login));
            }
          }
        }

        return new LogoutHookDetails() {
          OrganizationHooks = orgs,
          RepositoryHooks = repos,
        };
      });
    }

    private static SqlParameter CreateItemListTable<T>(string parameterName, IEnumerable<T> values) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[ItemListTableType]",
        new[] { ("Item", typeof(T)) },
        x => new object[] { x },
        values);
    }

    private static SqlParameter CreateCommentTable(string parameterName, IEnumerable<CommentTableType> comments) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[CommentTableType]",
        new[] {
          ("Id", typeof(long)),
          ("IssueId", typeof(long)),
          ("IssueNumber", typeof(int)),
          ("UserId", typeof(long)),
          ("Body", typeof(string)),
          ("CreatedAt", typeof(DateTimeOffset)),
          ("UpdatedAt", typeof(DateTimeOffset)),
        },
        x => new object[] {
          x.Id,
          x.IssueId,
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
          ("Id", typeof(long)),
          ("Color", typeof(string)),
          ("Name", typeof(string)),
        },
        x => new object[] {
          x.Id,
          x.Color,
          x.Name,
        },
        labels);
    }

    private static SqlParameter CreateIssueMappingTable(string parameterName, IEnumerable<IssueMappingTableType> mappings) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[IssueMappingTableType]",
        new[] {
          ("IssueNumber", typeof(int)),
          ("IssueId", typeof(long)),
          ("MappedId", typeof(long)),
        },
        x => new object[] {
          x.IssueNumber,
          x.IssueId,
          x.MappedId,
        },
        mappings);
    }

    private static SqlParameter CreateMappingTable(string parameterName, IEnumerable<MappingTableType> mappings) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[MappingTableType]",
        new[] {
          ("Item1", typeof(long)),
          ("Item2", typeof(long)),
        },
        x => new object[] {
          x.Item1,
          x.Item2,
        },
        mappings);
    }

    private static SqlParameter CreateVersionTableType(string parameterName, IEnumerable<VersionTableType> versions) {
      return CreateTableParameter(
        parameterName,
        "[dbo].[VersionTableType]",
        new[] {
          ("ItemId", typeof(long)),
          ("RowVersion", typeof(long)),
        },
        x => new object[] {
          x.ItemId,
          x.RowVersion,
        },
        versions);
    }

    private static SqlParameter CreateTableParameter<T>(string parameterName, string typeName, IEnumerable<(string ColumnName, Type DataType)> columns, Func<T, object[]> rowValues, IEnumerable<T> rows) {
      if (!typeName.Contains("[")) {
        typeName = $"[dbo].[{typeName}]";
      }

      DataTable table = null;

      try {
        if (rows != null) {
          table = new DataTable();

          table.Columns.AddRange(columns.Select(x => new DataColumn(x.ColumnName, x.DataType)).ToArray());

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

  public class WebhookDetails {
    public long HookId { get; set; }
    public string Name { get; set; }

    public WebhookDetails(long hookId, string name) {
      HookId = hookId;
      Name = name;
    }
  }

  public class LogoutHookDetails {
    public IEnumerable<WebhookDetails> RepositoryHooks { get; set; }
    public IEnumerable<WebhookDetails> OrganizationHooks { get; set; }
  }
}
