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

  -- Inform the client of any repos and orgs they can no longer access
  SELECT ItemId as RepositoryId
  FROM @RepositoryVersions as rv
  WHERE NOT EXISTS(SELECT 1 FROM AccountRepositories WHERE AccountId = @UserId AND RepositoryId = rv.ItemId)
  OPTION (RECOMPILE)

  SELECT ItemId as OrganizationId
  FROM @OrganizationVersions as ov
  WHERE NOT EXISTS(SELECT 1 FROM AccountOrganizations WHERE UserId = @UserId AND OrganizationId = ov.ItemId)
  OPTION (RECOMPILE)

  -- Start sync!
  DECLARE @AllRepoLogs TABLE (
    [RowNumber]    BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [RepositoryId] BIGINT       NOT NULL,
    [Type]         NVARCHAR(20) NOT NULL,
    [ItemId]       BIGINT       NOT NULL,
    [Delete]       BIT          NOT NULL,
    [RowVersion]   BIGINT       NOT NULL
  )

  DECLARE @RepoLogs TABLE (
    [RowNumber]    BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [Type]         NVARCHAR(20) NOT NULL INDEX IX_Type NONCLUSTERED,
    [ItemId]       BIGINT       NOT NULL,
    [Delete]       BIT          NOT NULL
  )

  DECLARE @RepoVersions TABLE (
    [RowNumber]    BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [RepositoryId] BIGINT       NOT NULL,
    [RowVersion]   BIGINT       NOT NULL
  )

  -- Select relevant logs
  INSERT INTO @AllRepoLogs
  SELECT ROW_NUMBER() OVER (ORDER BY rl.[RowVersion] ASC) as RowNumber, rl.RepositoryId, rl.[Type], rl.ItemId, rl.[Delete], rl.[RowVersion]
  FROM RepositoryLog as rl
    INNER JOIN AccountRepositories as ar ON (ar.RepositoryId = rl.RepositoryId)
    LEFT OUTER JOIN @RepositoryVersions as rv ON (rv.ItemId = rl.RepositoryId)
  WHERE ar.AccountId = @UserId
    AND ISNULL(rv.[RowVersion], 0) < rl.[RowVersion]

  -- Split out versions
  INSERT INTO @RepoVersions
  SELECT [RowNumber], RepositoryId, [RowVersion]
  FROM @AllRepoLogs

  -- Split out and dedup references
  INSERT INTO @RepoLogs
  SELECT RowNumber, [Type], ItemId, [Delete]
  FROM @AllRepoLogs

  ;WITH logs as (
    SELECT ROW_NUMBER() OVER (PARTITION BY [Type], ItemId ORDER BY RowNumber ASC) as RowNumber
    FROM @RepoLogs
  )
  DELETE FROM logs WHERE RowNumber > 1

  -- Done with work table
  DELETE @AllRepoLogs

  DECLARE @TotalLogs BIGINT, @RealLogs BIGINT;
  SELECT @TotalLogs = COUNT(1) FROM @RepoVersions;
  SELECT @RealLogs = COUNT(1) FROM @RepoLogs;

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
    SELECT [e].[Id], [e].[Type], [e].[Login]
    FROM Accounts as e
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'account')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Comments
    SELECT [e].[Id], [e].[IssueId], [e].[RepositoryId], [e].[UserId], [e].[Body], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[Reactions], [l].[Delete]
    FROM Comments as e
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'comment')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Events
    SELECT [e].[Id], [e].[RepositoryId], [e].[IssueId], [e].[ActorId], [e].[CommitId], [e].[Event],
           [e].[CreatedAt], [e].[AssigneeId], [e].[ExtensionData]
    FROM IssueEvents as e
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'event')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Milestones
    SELECT [e].[Id], [e].[RepositoryId], [e].[Number], [e].[State], [e].[Title], [e].[Description],
           [e].[CreatedAt], [e].[UpdatedAt], [e].[ClosedAt], [e].[DueOn], [l].[Delete]
    FROM Milestones as e
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'milestone')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Begin Issues ---------------------------------------------
    -- Issue Labels
    SELECT il.IssueId, labels.Name, labels.Color
    FROM Labels as labels
      INNER JOIN IssueLabels as il ON (labels.Id = il.LabelId)
      INNER JOIN @RepoLogs as l ON (il.IssueId = l.ItemId AND l.[Type] = 'issue')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Issues
    SELECT [e].[Id], [e].[UserId], [e].[RepositoryId], [e].[Number], [e].[State], [e].[Title],
           [e].[Body], [e].[AssigneeId], [e].[MilestoneId], [e].[Locked], [e].[CreatedAt],
           [e].[UpdatedAt], [e].[ClosedAt], [e].[ClosedById], [e].[Reactions]
    FROM Issues as e
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'issue')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)
    -- End Issues ---------------------------------------------

    -- Begin Repositories ---------------------------------------------
    -- Repository Labels
    SELECT rl.RepositoryId, labels.Name, labels.Color
    FROM Labels as labels
      INNER JOIN RepositoryLabels as rl ON (labels.Id = rl.LabelId)
      INNER JOIN @RepoLogs as l ON (rl.RepositoryId = l.ItemId AND l.[Type] = 'repository')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Repository Assignable Users
    SELECT ra.RepositoryId, ra.AccountId
    FROM RepositoryAccounts as ra
      INNER JOIN @RepoLogs as l ON (ra.RepositoryId = l.ItemId AND l.[Type] = 'repository')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)

    -- Repositories
    SELECT [e].[Id], [e].[AccountId], [e].[Private], [e].[Name], [e].[FullName]
    FROM Repositories as e
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'repository')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    OPTION (RECOMPILE)
    -- End Repositories ---------------------------------------------

    -- Current Versions
    SELECT RepositoryId, MAX([RowVersion]) as [RowVersion]
    FROM @RepoVersions
    WHERE RowNumber BETWEEN @WindowBegin AND @WindowEnd
    GROUP BY RepositoryId
    OPTION (RECOMPILE)

    SET @WindowBegin = @WindowBegin + @PageSize
    SET @WindowEnd = @WindowEnd + @PageSize
  END

  -- Orgs
  -- Not presently paginated.

  DECLARE @OrgLogs TABLE (
    [OrganizationId] BIGINT NOT NULL,
    [AccountId]      BIGINT NOT NULL,
    [RowVersion]     BIGINT NOT NULL,
    UNIQUE CLUSTERED (OrganizationId, AccountId)
  )

  -- Select relevant logs
  INSERT INTO @OrgLogs
  SELECT ol.OrganizationId, ol.AccountId, ol.[RowVersion]
  FROM OrganizationLog as ol
    INNER JOIN AccountOrganizations as ao ON (ao.OrganizationId = ol.OrganizationId)
    LEFT OUTER JOIN @OrganizationVersions as ov ON (ov.ItemId = ol.OrganizationId)
  WHERE ao.UserId = @UserId
    AND ISNULL(ov.[RowVersion], 0) < ol.[RowVersion]

  -- Mark as repo logs
  SELECT 2 as [Type]

  -- Accounts
  -- Return org itself as well
  SELECT DISTINCT e.Id, e.[Type], e.[Login]
  FROM Accounts as e
    INNER JOIN @OrgLogs as l ON (e.Id = l.AccountId OR e.Id = l.OrganizationId)
  OPTION (RECOMPILE)

  -- Membership for updated orgs
  SELECT ao.OrganizationId, ao.UserId
  FROM AccountOrganizations as ao
  WHERE EXISTS (SELECT 1 FROM @OrgLogs as l WHERE l.OrganizationId = ao.OrganizationId)

  -- Org Versions
  SELECT OrganizationId, MAX([RowVersion]) as [RowVersion]
  FROM @OrgLogs
  GROUP BY OrganizationId
END
