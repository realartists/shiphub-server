CREATE PROCEDURE [dbo].[WhatsNew]
  @Token NVARCHAR(64),
  @PageSize BIGINT = 1000,
  @RepositoryVersions VersionTableType READONLY,
  @OrganizationVersions VersionTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @UserId BIGINT
  SELECT @UserId = Id FROM Accounts WHERE Token = @Token

  -- If this is null the app knows the token is invalid.
  SELECT @UserId as UserId

  IF (@UserId IS NULL) RETURN

  -- Inform the client of any repos and orgs they can no longer access
  SELECT ItemId as RepositoryId
  FROM @RepositoryVersions as rv
  WHERE NOT EXISTS (SELECT * FROM AccountRepositories WHERE AccountId = @UserId AND RepositoryId = rv.ItemId AND [Hidden] = 0)

  SELECT ItemId as OrganizationId
  FROM @OrganizationVersions as ov
  WHERE NOT EXISTS (SELECT * FROM OrganizationAccounts WHERE UserId = @UserId AND OrganizationId = ov.ItemId)

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
    [RowNumber]    BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [RepositoryId] BIGINT NOT NULL,
    [RowVersion]   BIGINT NOT NULL
  )

  -- Select relevant logs
  INSERT INTO @AllRepoLogs
  SELECT ROW_NUMBER() OVER (ORDER BY rl.[RowVersion] ASC) as RowNumber, rl.RepositoryId, rl.[Type], rl.ItemId, rl.[Delete], rl.[RowVersion]
  FROM RepositoryLog as rl
    INNER JOIN AccountRepositories as ar ON (ar.RepositoryId = rl.RepositoryId)
    LEFT OUTER JOIN @RepositoryVersions as rv ON (rv.ItemId = rl.RepositoryId)
  WHERE ar.AccountId = @UserId
    AND ar.[Hidden] = 0
    AND ISNULL(rv.[RowVersion], 0) < rl.[RowVersion]

  -- Split out versions
  INSERT INTO @RepoVersions
  SELECT RowNumber, RepositoryId, [RowVersion]
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

  DECLARE @TotalLogs BIGINT, @RealLogs BIGINT
  SELECT @TotalLogs = COUNT(*) FROM @RepoVersions
  SELECT @RealLogs = COUNT(*) FROM @RepoLogs

  IF @PageSize < 100
  BEGIN
    SET @PageSize = 100
  END

  DECLARE  @WindowBegin BIGINT = 1,
           @WindowEnd   BIGINT = @PageSize

  -- Total number of log entries to be returned
  -- Contains duplicates
  SELECT @RealLogs as TotalLogs

  WHILE @WindowBegin <= @TotalLogs -- When there's a single log entry to sync, @WindowBegin = @TotalLogs
  BEGIN
    -- Mark as repo logs
    SELECT 1 as [Type]

    -- Accounts
    SELECT e.Id, e.[Type], e.[Login]
    FROM Accounts as e
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'account')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd

    -- Comments
    SELECT l.ItemId as Id, e.IssueId, e.RepositoryId, e.UserId, e.Body, e.CreatedAt,
           e.UpdatedAt, l.[Delete]
    FROM @RepoLogs as l
      LEFT OUTER JOIN Comments as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.[Type] = 'comment'

    -- Events
    SELECT e.Id, e.RepositoryId, e.IssueId, e.ActorId, e.[Event], e.CreatedAt, e.ExtensionData,
           CAST(CASE WHEN a.UserId IS NULL THEN e.Restricted ELSE 0 END as BIT) as Restricted
    FROM IssueEvents as e
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'event')
      LEFT OUTER JOIN IssueEventAccess as a ON (e.Restricted = 1 AND a.IssueEventId = e.Id AND a.UserId = @UserId)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd

    -- Milestones
    SELECT l.ItemId as Id, e.RepositoryId, e.Number, e.[State], e.Title, e.[Description],
           e.CreatedAt, e.UpdatedAt, e.ClosedAt, e.DueOn, l.[Delete]
    FROM @RepoLogs as l
      LEFT OUTER JOIN Milestones as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.[Type] = 'milestone'

    -- Reactions
    SELECT l.ItemId as Id, e.UserId, e.IssueId, e.CommentId, e.Content, e.CreatedAt, l.[Delete]
    FROM @RepoLogs as l
      LEFT OUTER JOIN Reactions as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.[Type] = 'reaction'

    -- Labels
    SELECT l.ItemId as Id, e.RepositoryId, e.Color, e.Name, l.[Delete]
    FROM @RepoLogs as l
      LEFT OUTER JOIN Labels as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.[Type] = 'label'

    -- Begin Issues ---------------------------------------------
    -- Issue Labels
    SELECT il.IssueId, il.LabelId
    FROM IssueLabels as il
      INNER JOIN @RepoLogs as l ON (il.IssueId = l.ItemId AND l.[Type] = 'issue')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd

    -- Issue Assignees
    SELECT IssueId, UserId
    FROM IssueAssignees
      INNER JOIN @RepoLogs as l ON (IssueId = l.ItemId AND l.[Type] = 'issue')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd

    -- Issues
    SELECT e.Id, e.UserId, e.RepositoryId, e.Number, e.[State], e.Title,
           e.Body, e.MilestoneId, e.Locked, e.CreatedAt, e.UpdatedAt,
           e.ClosedAt, e.ClosedById, e.PullRequest, e.Reactions
    FROM Issues as e
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'issue')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    -- End Issues ---------------------------------------------

    -- Begin Repositories ---------------------------------------------
    -- Repository Assignable Users
    SELECT ra.RepositoryId, ra.AccountId
    FROM RepositoryAccounts as ra
      INNER JOIN @RepoLogs as l ON (ra.RepositoryId = l.ItemId AND l.[Type] = 'repository')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd

    -- Repositories
    SELECT e.Id, e.AccountId, e.[Private], e.Name, e.FullName, e.IssueTemplate, ar.[Admin],
      CAST (CASE WHEN h.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasHook
    FROM Repositories as e
      INNER JOIN AccountRepositories as ar ON (ar.RepositoryId = e.Id AND ar.AccountId = @UserId)
      LEFT OUTER JOIN Hooks AS h ON (h.RepositoryId = e.Id)
      INNER JOIN @RepoLogs as l ON (e.Id = l.ItemId AND l.[Type] = 'repository')
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
    -- End Repositories ---------------------------------------------

    -- Current Versions
    SELECT RepositoryId, MAX([RowVersion]) as [RowVersion]
    FROM @RepoVersions
    WHERE RowNumber BETWEEN @WindowBegin AND @WindowEnd
    GROUP BY RepositoryId

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
    INNER JOIN OrganizationAccounts as oa ON (oa.OrganizationId = ol.OrganizationId)
    LEFT OUTER JOIN @OrganizationVersions as ov ON (ov.ItemId = ol.OrganizationId)
  WHERE oa.UserId = @UserId
    AND ISNULL(ov.[RowVersion], 0) < ol.[RowVersion]

  -- Mark as org logs
  SELECT 2 as [Type]

  -- Accounts
  -- Return org itself as well
  SELECT DISTINCT e.Id, e.[Type], e.[Login],
    -- HasHook + Admin only apply to orgs.
    CAST(CASE WHEN h.Id IS NOT NULL THEN 1 ELSE 0 END as BIT) as HasHook,
    CAST(ISNULL(oa.[Admin], 0) as BIT) as [Admin]
  FROM Accounts as e
    INNER JOIN @OrgLogs as l ON (e.Id = l.AccountId OR e.Id = l.OrganizationId)
    LEFT OUTER JOIN Hooks as h ON (e.[Type] = 'org' AND h.OrganizationId = e.Id)
    LEFT OUTER JOIN OrganizationAccounts as oa ON (e.[Type] = 'org' AND oa.OrganizationId = e.Id AND oa.UserId = @UserId)

  -- Membership for updated orgs
  SELECT oa.OrganizationId, oa.UserId
  FROM OrganizationAccounts as oa
  WHERE EXISTS (SELECT * FROM @OrgLogs as l WHERE l.OrganizationId = oa.OrganizationId)

  -- Org Versions
  SELECT OrganizationId, MAX([RowVersion]) as [RowVersion]
  FROM @OrgLogs
  GROUP BY OrganizationId
END
