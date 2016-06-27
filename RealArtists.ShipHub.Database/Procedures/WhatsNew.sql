CREATE PROCEDURE [dbo].[WhatsNew]
  @UserId BIGINT,
  @PageSize BIGINT = 1000,
  @RepositoryVersions VersionTableType READONLY,
  @OrganizationVersions VersionTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @AllLogs TABLE (
    [RowNumber]    BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [RepositoryId] BIGINT       NOT NULL,
    [Type]         NVARCHAR(20) NOT NULL,
    [ItemId]       BIGINT       NOT NULL,
    [Delete]       BIT          NOT NULL,
    [RowVersion]   BIGINT       NOT NULL
  )

  DECLARE @Logs TABLE (
    [RowNumber]    BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [Type]         NVARCHAR(20) NOT NULL INDEX IX_Type NONCLUSTERED,
    [ItemId]       BIGINT       NOT NULL,
    [Delete]       BIT          NOT NULL
  )

  DECLARE @Versions TABLE (
    [RowNumber]    BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [RepositoryId] BIGINT       NOT NULL,
    [RowVersion]   BIGINT       NOT NULL
  )

  -- Select relevant logs
  INSERT INTO @AllLogs
  SELECT ROW_NUMBER() OVER (ORDER BY rl.[RowVersion] ASC) as RowNumber, rl.RepositoryId, rl.[Type], rl.ItemId, rl.[Delete], rl.[RowVersion]
    FROM RepositoryLog as rl
      INNER JOIN AccountRepositories as ar ON (ar.RepositoryId = rl.RepositoryId)
      LEFT OUTER JOIN @RepositoryVersions as rv ON (rv.ItemId = rl.RepositoryId)
  WHERE ar.AccountId = @UserId
    AND ISNULL(rv.[RowVersion], 0) < rl.[RowVersion]

  -- Split out versions
  INSERT INTO @Versions
  SELECT [RowNumber], RepositoryId, [RowVersion]
    FROM @AllLogs

  -- Split out and dedup references
  INSERT INTO @Logs
  SELECT RowNumber, [Type], ItemId, [Delete]
    FROM @AllLogs

  ;WITH logs as (
    SELECT ROW_NUMBER() OVER (PARTITION BY [Type], ItemId ORDER BY RowNumber ASC) as RowNumber
    FROM @Logs
  )
  DELETE FROM logs WHERE RowNumber > 1

  -- Done with work table
  DELETE @AllLogs

  DECLARE @TotalLogs BIGINT, @RealLogs BIGINT;
  SELECT @TotalLogs = COUNT(1) FROM @Versions;
  SELECT @RealLogs = COUNT(1) FROM @Logs;

  IF @PageSize < 100
  BEGIN
    SET @PageSize = 100
  END

  DECLARE  @WindowBegin BIGINT = 1,
           @WindowEnd   BIGINT = @PageSize;

  -- Total number of log entries to be returned
  -- Contains duplicates
  SELECT @RealLogs as TotalLogs

  WHILE @WindowBegin < @TotalLogs
  BEGIN
    -- Mark as repo logs
    SELECT 1 as [Type]

    -- Accounts
    SELECT DISTINCT [e].[Id], [e].[Type], [e].[Login]
    FROM Accounts as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'account')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Comments
    SELECT [e].[Id], [e].[IssueId], [e].[RepositoryId], [e].[UserId], [e].[Body], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[Reactions], [l].[Delete]
    FROM Comments as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'comment')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Events
    SELECT [e].[Id], [e].[RepositoryId], [e].[IssueId], [e].[ActorId], [e].[CommitId], [e].[Event],
           [e].[CreatedAt], [e].[AssigneeId], [e].[ExtensionData]
    FROM IssueEvents as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'event')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Milestones
    SELECT [e].[Id], [e].[RepositoryId], [e].[Number], [e].[State], [e].[Title], [e].[Description],
           [e].[CreatedAt], [e].[UpdatedAt], [e].[ClosedAt], [e].[DueOn], [l].[Delete]
    FROM Milestones as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'milestone')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Begin Issues ---------------------------------------------
    -- Issue Labels
    SELECT il.IssueId, labels.Name, labels.Color
    FROM Labels as labels
      INNER JOIN IssueLabels as il ON (labels.Id = il.LabelId)
      INNER JOIN @Logs as l ON (il.IssueId = l.ItemId AND l.[Type] = 'issue')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Issues
    SELECT [e].[Id], [e].[UserId], [e].[RepositoryId], [e].[Number], [e].[State], [e].[Title],
           [e].[Body], [e].[AssigneeId], [e].[MilestoneId], [e].[Locked], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[ClosedAt], [e].[ClosedById], [e].[Reactions]
    FROM Issues as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'issue')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)
    -- End Issues ---------------------------------------------

    -- Begin Repositories ---------------------------------------------
    -- Repository Labels
    SELECT rl.RepositoryId, labels.Name, labels.Color
    FROM Labels as labels
      INNER JOIN RepositoryLabels as rl ON (labels.Id = rl.LabelId)
      INNER JOIN @Logs as l ON (rl.RepositoryId = l.ItemId AND l.[Type] = 'repository')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Repository Assignable Users
    SELECT ra.RepositoryId, ra.AccountId
    FROM RepositoryAccounts as ra
      INNER JOIN @Logs as l ON (ra.RepositoryId = l.ItemId AND l.[Type] = 'repository')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Repositories
    SELECT [e].[Id], [e].[AccountId], [e].[Private], [e].[Name], [e].[FullName]
    FROM Repositories as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'repository')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)
    -- End Repositories ---------------------------------------------

    -- Current Versions
    SELECT RepositoryId, MAX([RowVersion]) as [RowVersion]
    FROM @Versions
    WHERE RowNumber BETWEEN @WindowBegin AND @WindowEnd
    GROUP BY RepositoryId
    OPTION (RECOMPILE)

    SET @WindowBegin = @WindowBegin + @PageSize
    SET @WindowEnd = @WindowEnd + @PageSize
  END

  -- TODO: Orgs
END
