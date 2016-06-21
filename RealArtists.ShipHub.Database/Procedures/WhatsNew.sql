CREATE PROCEDURE [dbo].[WhatsNew]
  @Token NVARCHAR(64),
  @RepositoryVersions VersionTableType READONLY,
  @OrganizationVersions VersionTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @AccountId BIGINT
  SELECT @AccountId = AccountId FROM AccessTokens WHERE Token = @Token

  DECLARE @Logs TABLE (
    [RowNumber]    BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [RepositoryId] BIGINT       NOT NULL,
    [Type]         NVARCHAR(20) NOT NULL INDEX IX_Type NONCLUSTERED,
    [ItemId]       BIGINT       NOT NULL,
    [Delete]       BIT          NOT NULL,
    [RowVersion]   BIGINT       NOT NULL
  )

  INSERT INTO @Logs
  SELECT ROW_NUMBER() OVER (ORDER BY rl.[RowVersion] ASC) as RowNumber, rl.RepositoryId, rl.[Type], rl.ItemId, rl.[Delete], rl.[RowVersion]
    FROM RepositoryLog as rl
      INNER JOIN AccountRepositories as ar ON (ar.RepositoryId = rl.RepositoryId)
      LEFT OUTER JOIN @RepositoryVersions as rv ON (rv.ItemId = rl.RepositoryId)
    WHERE ar.AccountId = @AccountId
      AND ISNULL(rv.[RowVersion], 0) < rl.[RowVersion]
  OPTION (RECOMPILE)

  DECLARE
    @TotalLogs BIGINT = @@ROWCOUNT,
    @PageSize BIGINT = 1000,
    @WindowBegin BIGINT = 1;

  DECLARE @WindowEnd BIGINT = @PageSize;

  -- Total number of log entries to be returned
  SELECT @TotalLogs as TotalLogs

  WHILE @WindowBegin < @TotalLogs
  BEGIN
    -- Accounts
    SELECT [e].[Id], [e].[Type], [e].[Login], [e].[Date], [e].[RepositoryMetaDataId]
    FROM Accounts as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId)
    WHERE l.[Type] = 'account'
      AND l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Comments
    SELECT [e].[Id], [e].[IssueId], [e].[RepositoryId], [e].[UserId], [e].[Body], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[Reactions]
    FROM Comments as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId)
    WHERE l.[Type] = 'comment'
      AND l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Events
    SELECT [e].[Id], [e].[RepositoryId], [e].[IssueId], [e].[ActorId], [e].[CommitId], [e].[Event],
           [e].[CreatedAt], [e].[AssigneeId], [e].[ExtensionData]
    FROM IssueEvents as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId)
    WHERE l.[Type] = 'event'
      AND l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Issues
    SELECT [e].[Id], [e].[UserId], [e].[RepositoryId], [e].[Number], [e].[State], [e].[Title],
           [e].[Body], [e].[AssigneeId], [e].[MilestoneId], [e].[Locked], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[ClosedAt], [e].[ClosedById], [e].[Reactions], [e].[MetaDataId]
    FROM Issues as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId)
    WHERE l.[Type] = 'issue'
      AND l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Milestone
    SELECT [e].[Id], [e].[UserId], [e].[RepositoryId], [e].[Number], [e].[State], [e].[Title],
           [e].[Body], [e].[AssigneeId], [e].[MilestoneId], [e].[Locked], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[ClosedAt], [e].[ClosedById], [e].[Reactions], [e].[MetaDataId]
    FROM Issues as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId)
    WHERE l.[Type] = 'milestone'
      AND l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Repository
    SELECT [e].[Id], [e].[UserId], [e].[RepositoryId], [e].[Number], [e].[State], [e].[Title],
           [e].[Body], [e].[AssigneeId], [e].[MilestoneId], [e].[Locked], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[ClosedAt], [e].[ClosedById], [e].[Reactions], [e].[MetaDataId]
    FROM Issues as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId)
    WHERE l.[Type] = 'repository'
      AND l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    SET @WindowBegin = @WindowBegin + @PageSize
  END

  -- TODO: Orgs
END
