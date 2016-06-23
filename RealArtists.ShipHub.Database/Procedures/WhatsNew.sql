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
    WHERE ar.AccountId = @UserId
      AND ISNULL(rv.[RowVersion], 0) < rl.[RowVersion]
  OPTION (RECOMPILE)

  DECLARE
    @TotalLogs BIGINT = @@ROWCOUNT,
    @WindowBegin BIGINT = 1;

  DECLARE @WindowEnd BIGINT = @PageSize;

  -- Total number of log entries to be returned
  SELECT @TotalLogs as TotalLogs

  WHILE @WindowBegin < @TotalLogs
  BEGIN
    -- Accounts
    SELECT [e].[Id], [e].[Type], [e].[Login], [l].[Delete], [l].[RowVersion], [l].[RepositoryId]
    FROM Accounts as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'account')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Comments
    SELECT [e].[Id], [e].[IssueId], [e].[RepositoryId], [e].[UserId], [e].[Body], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[Reactions], [l].[Delete], [l].[RowVersion], [l].[RepositoryId]
    FROM Comments as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'comment')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Events
    SELECT [e].[Id], [e].[RepositoryId], [e].[IssueId], [e].[ActorId], [e].[CommitId], [e].[Event],
           [e].[CreatedAt], [e].[AssigneeId], [e].[ExtensionData], [l].[Delete], [l].[RowVersion],
           [l].[RepositoryId]
    FROM IssueEvents as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'event')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Issues
    SELECT [e].[Id], [e].[UserId], [e].[RepositoryId], [e].[Number], [e].[State], [e].[Title],
           [e].[Body], [e].[AssigneeId], [e].[MilestoneId], [e].[Locked], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[ClosedAt], [e].[ClosedById], [e].[Reactions], [e].[MetaDataId],
           [l].[Delete], [l].[RowVersion], [l].[RepositoryId]
    FROM Issues as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'issue')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Milestone
    SELECT [e].[Id], [e].[RepositoryId], [e].[Number], [e].[State], [e].[Title], [e].[Description],
           [e].[CreatedAt], [e].[UpdatedAt], [e].[ClosedAt], [e].[DueOn], [l].[Delete],
           [l].[RowVersion], [l].[RepositoryId]
    FROM Milestones as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'milestone')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Repository
    SELECT [e].[Id], [e].[AccountId], [e].[Private], [e].[Name], [e].[FullName], [l].[Delete],
           [l].[RowVersion], [l].[RepositoryId]
    FROM Repositories as e
      INNER JOIN @Logs as l ON (e.Id = l.ItemId AND l.[Type] = 'repository')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Current Versions
    SELECT RepositoryId, MAX([RowVersion]) as [RowVersion]
    FROM @Logs
    WHERE RowNumber BETWEEN @WindowBegin AND @WindowEnd
    GROUP BY RepositoryId
    OPTION (RECOMPILE)

    SET @WindowBegin = @WindowBegin + @PageSize
    SET @WindowEnd = @WindowEnd + @PageSize
  END

  -- TODO: Orgs
END
