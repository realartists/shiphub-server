CREATE PROCEDURE [dbo].[WhatsNew]
  @UserId BIGINT,
  @PageSize BIGINT = 1000,
  @RepositoryVersions VersionTableType READONLY,
  @OrganizationVersions VersionTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  IF (@PageSize < 100) SET @PageSize = 100
  IF (@PageSize > 2000) SET @PageSize = 2000

  -- Require the user to be a user
  IF (NOT EXISTS(SELECT * FROM Accounts WHERE Id = @UserId AND [Type] = 'user')) RETURN
  -- Require the user to have an active token
  IF (NOT EXISTS(SELECT * FROM GitHubTokens WHERE UserId = @UserId)) RETURN

  -- ------------------------------------------------------------------------------------------------------------------
  -- Log Prep
  -- ------------------------------------------------------------------------------------------------------------------

  -- This table holds all the logs that pertain to the current user.
  -- It will reference entities multiple time, and must be paired down,
  -- but we need a stable capture of all the information to compute
  -- the RowNumber -> Owner Id, Version mappings.
  DECLARE @AllLogs TABLE (
    [RowNumber]  BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [RowVersion] BIGINT       NOT NULL,
    [OwnerType]  NVARCHAR(4)  NOT NULL,
    [OwnerId]    BIGINT       NOT NULL,
    [ItemType]   NVARCHAR(20) NOT NULL,
    [ItemId]     BIGINT       NOT NULL,
    [Delete]     BIT          NOT NULL
  )

  -- This table holds single references to all new/changed entities.
  DECLARE @Logs TABLE (
    [RowNumber] BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [ItemType]  NVARCHAR(20) NOT NULL,
    [ItemId]    BIGINT       NOT NULL,
    [Delete]    BIT          NOT NULL,
    INDEX IX_RowNumber_ItemType ([RowNumber], [ItemType]),
    INDEX IX_ItemType_ItemId ([ItemType], [ItemId])
  )

  -- This table holds the mappings from 
  -- [rows we've selected] -> [versions of owners]
  -- This lets us checkpoint at each page we send to the client.
  DECLARE @OwnerVersions TABLE (
    [RowNumber]  BIGINT      NOT NULL PRIMARY KEY CLUSTERED,
    [OwnerType]  NVARCHAR(4) NOT NULL,
    [OwnerId]    BIGINT      NOT NULL,
    [RowVersion] BIGINT      NOT NULL,
    INDEX IX_OwnerType_OwnerId ([OwnerType], [OwnerId])
  )

  -- Instead of using transactions, work off a single stable list of sync logs
  DECLARE @UserOrgs TABLE (
    [OrganizationId] BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [Admin]          BIT    NOT NULL
  )

  DECLARE @UpdatedOrgs TABLE (
    [OrganizationId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  -- Populate stable org list
  INSERT INTO @UserOrgs
  SELECT OrganizationId, [Admin] FROM OrganizationAccounts WHERE UserId = @UserId

  -- Populate work table with relevant logs
  -- TODO: Can this be made one statement (non union?)
  ;WITH LogViewForUser AS (
    SELECT sl.OwnerType, sl.OwnerId, sl.ItemType, sl.ItemId, sl.[Delete], sl.[RowVersion]
      FROM SyncLog as sl
      INNER JOIN AccountRepositories as ar ON (ar.RepositoryId = sl.OwnerId AND ar.AccountId = @UserId AND ar.[Hidden] = 0)
      LEFT OUTER JOIN @RepositoryVersions as rv ON (rv.ItemId = sl.OwnerId)
    WHERE sl.OwnerType = 'repo'
      AND ISNULL(rv.[RowVersion], 0) < sl.[RowVersion]
    UNION ALL
    SELECT sl.OwnerType, sl.OwnerId, sl.ItemType, sl.ItemId, sl.[Delete], sl.[RowVersion]
      FROM SyncLog as sl
      INNER JOIN @UserOrgs as uo ON (uo.OrganizationId = sl.OwnerId)
      LEFT OUTER JOIN @OrganizationVersions as ov ON (ov.ItemId = sl.OwnerId)
    WHERE sl.OwnerType = 'org'
      AND ISNULL(ov.[RowVersion], 0) < sl.[RowVersion]
  )
  INSERT INTO @AllLogs
  SELECT ROW_NUMBER() OVER (ORDER BY sl.[RowVersion] ASC) as RowNumber, sl.[RowVersion], sl.OwnerType, sl.OwnerId, sl.ItemType, sl.ItemId, sl.[Delete]
  FROM LogViewForUser as sl

  -- Split out versions
  INSERT INTO @OwnerVersions
  SELECT RowNumber, OwnerType, OwnerId, [RowVersion]
  FROM @AllLogs

  -- Only select the first occurrence of each entity.
  ;WITH PartitionedLogs AS (
    SELECT
      ROW_NUMBER() OVER (PARTITION BY ItemType, ItemId ORDER BY RowNumber ASC) as Occurrence,
      RowNumber, ItemType, ItemId, [Delete]
    FROM @AllLogs
  )
  INSERT INTO @Logs
  SELECT RowNumber, ItemType, ItemId, [Delete]
  FROM PartitionedLogs
  WHERE Occurrence = 1

  -- Done with work table
  DELETE @AllLogs

  -- Populate Updated Orgs
  INSERT INTO @UpdatedOrgs
  SELECT DISTINCT OwnerId FROM @OwnerVersions WHERE OwnerType = 'org'

  -- ------------------------------------------------------------------------------------------------------------------
  -- Basic User Info
  -- ------------------------------------------------------------------------------------------------------------------

  SELECT Id as UserId, RateLimit, RateLimitRemaining, RateLimitReset FROM Accounts WHERE Id = @UserId

  -- ------------------------------------------------------------------------------------------------------------------
  -- Deleted orgs and repos (permission removed or deleted)
  -- TODO: Unify these?
  -- ------------------------------------------------------------------------------------------------------------------
  
  SELECT ItemId as RepositoryId
  FROM @RepositoryVersions as rv
  WHERE NOT EXISTS (SELECT * FROM AccountRepositories WHERE AccountId = @UserId AND RepositoryId = rv.ItemId AND [Hidden] = 0)

  SELECT ov.ItemId as OrganizationId, a.[Login]
  FROM @OrganizationVersions as ov
    LEFT OUTER JOIN @UserOrgs as uo ON (uo.OrganizationId = ov.ItemId)
    LEFT OUTER JOIN Accounts as a ON (a.Id = ov.ItemId)
  WHERE uo.OrganizationId IS NULL

  -- ------------------------------------------------------------------------------------------------------------------
  -- New/Updated Orgs
  -- ------------------------------------------------------------------------------------------------------------------

  SELECT e.Id, e.[Type], e.[Login], uo.[Admin],
    CAST(CASE WHEN h.GitHubId IS NOT NULL THEN 1 ELSE 0 END as BIT) as HasHook
  FROM @UpdatedOrgs as u
    INNER JOIN @UserOrgs as uo ON (uo.OrganizationId = u.OrganizationId)
    INNER LOOP JOIN Accounts as e ON (e.Id = u.OrganizationId)
    LEFT OUTER LOOP JOIN Hooks as h ON (h.OrganizationId = e.Id)
  OPTION (FORCE ORDER)

  -- Membership for updated orgs
  SELECT oa.OrganizationId, oa.UserId
  FROM @UpdatedOrgs as uo
    INNER LOOP JOIN OrganizationAccounts as oa ON (oa.OrganizationId = uo.OrganizationId)
  OPTION (FORCE ORDER)

  -- Version updates occur as entities sync below

  -- ------------------------------------------------------------------------------------------------------------------
  -- New/Updated entites (paginated)
  -- ------------------------------------------------------------------------------------------------------------------

  DECLARE @TotalLogs BIGINT
  SELECT @TotalLogs = COUNT(*) FROM @OwnerVersions

  DECLARE @WindowBegin BIGINT = 1,
          @WindowEnd   BIGINT = @PageSize

  -- Total number of actual log entries to be returned (deduplicated)
  SELECT COUNT(*) as TotalEntries FROM @Logs

  WHILE @WindowBegin <= @TotalLogs -- When there's a single log entry to sync, @WindowBegin = @TotalLogs
  BEGIN
    -- Accounts
    SELECT e.Id, e.[Type], e.[Login]
    FROM Accounts as e
      INNER JOIN @Logs as l ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'account'

    -- Commit Comments
    SELECT l.ItemId as Id, e.RepositoryId, e.UserId, e.CommitId,
      e.[Path], e.Line, e.Position, e.Body, e.CreatedAt, e.UpdatedAt, l.[Delete]
    FROM @Logs as l
      LEFT OUTER JOIN CommitComments as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'commitcomment'

    -- Issue Comments
    SELECT l.ItemId as Id, e.IssueId, e.RepositoryId, e.UserId, e.Body, e.CreatedAt, e.UpdatedAt, l.[Delete]
    FROM @Logs as l
      LEFT OUTER JOIN Comments as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'comment'

    -- Events
    SELECT e.Id, e.RepositoryId, e.IssueId, e.ActorId, e.[Event], e.CreatedAt, e.ExtensionData,
           CAST(CASE WHEN a.UserId IS NULL THEN e.Restricted ELSE 0 END as BIT) as Restricted
    FROM @Logs as l
      INNER JOIN IssueEvents as e ON (l.ItemId = e.Id)
      LEFT OUTER JOIN IssueEventAccess as a ON (e.Restricted = 1 AND a.IssueEventId = e.Id AND a.UserId = @UserId)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'event'

    -- Milestones
    SELECT l.ItemId as Id, e.RepositoryId, e.Number, e.[State], e.Title, e.[Description],
           e.CreatedAt, e.UpdatedAt, e.ClosedAt, e.DueOn, l.[Delete]
    FROM @Logs as l
      LEFT OUTER JOIN Milestones as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'milestone'

    -- Projects
    SELECT l.ItemId as Id, e.Name, e.Number, e.Body, e.CreatedAt, e.UpdatedAt,
           e.CreatorId, e.OrganizationId, e.RepositoryId, l.[Delete]
    FROM @Logs as l
      LEFT OUTER JOIN Projects as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'project'

    -- Reactions
    SELECT l.ItemId as Id, e.UserId, e.IssueId, e.CommentId, e.PullRequestCommentId,
           e.CommitCommentId, e.Content, e.CreatedAt, l.[Delete]
    FROM @Logs as l
      LEFT OUTER JOIN Reactions as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'reaction'

    -- Labels
    SELECT l.ItemId as Id, e.RepositoryId, e.Color, e.Name, l.[Delete]
    FROM @Logs as l
      LEFT OUTER JOIN Labels as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'label'

    -- Begin Issues ---------------------------------------------
    -- Issue Labels
    SELECT e.IssueId, e.LabelId
    FROM @Logs as l
      INNER JOIN IssueLabels as e ON (l.ItemId = e.IssueId)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'issue'

    -- Issue Assignees
    SELECT e.IssueId, e.UserId
    FROM @Logs as l
      INNER JOIN IssueAssignees as e ON (l.ItemId = e.IssueId)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'issue'

    -- Pull Request Reviewers
    SELECT e.IssueId, e.UserId
    FROM @Logs as l
      INNER JOIN PullRequestReviewers as e ON (l.ItemId = e.IssueId)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'issue'

    -- Issues
    SELECT e.Id, e.UserId, e.RepositoryId, e.Number, e.[State], e.Title,
           e.Body, e.MilestoneId, e.Locked, e.CreatedAt, e.UpdatedAt,
           e.ClosedAt, e.ClosedById, e.PullRequest, e.Reactions,
           -- PRs
           pr.Id as PullRequestId, pr.CreatedAt as PullRequestCreatedAt,
           pr.UpdatedAt as PullRequestUpdatedAt, pr.MergeCommitSha, pr.MergedAt,
           pr.BaseJson, pr.HeadJson, pr.Additions, pr.ChangedFiles, pr. Commits,
           pr.Deletions, pr.MaintainerCanModify, pr.Mergeable, pr.MergeableState,
           pr.MergedById, pr.Rebaseable
    FROM @Logs as l
      INNER JOIN Issues as e ON (l.ItemId = e.Id)
      LEFT OUTER JOIN PullRequests as pr ON (pr.IssueId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'issue'
    -- End Issues ---------------------------------------------

    -- Begin Repositories ---------------------------------------------
    -- Repository Assignable Users
    SELECT ra.RepositoryId, ra.AccountId
    FROM @Logs as l
      INNER JOIN RepositoryAccounts as ra ON (l.ItemId = ra.RepositoryId)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'repository'

    -- Repositories
    SELECT e.Id, e.AccountId, e.[Private], e.Name, e.FullName, e.IssueTemplate, ar.[Admin],
      e.[Disabled], CAST (CASE WHEN h.GitHubId IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasHook
    FROM @Logs as l
      INNER JOIN Repositories as e ON (l.ItemId = e.Id)
      INNER JOIN AccountRepositories as ar ON (ar.RepositoryId = e.Id AND ar.AccountId = @UserId)
      LEFT OUTER JOIN Hooks AS h ON (h.RepositoryId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'repository'
    -- End Repositories ---------------------------------------------

    -- Pull Request Reviews
    SELECT l.ItemId as Id, e.IssueId, e.UserId, e.Body,
      e.CommitId, e.[State], e.SubmittedAt, l.[Delete]
    FROM @Logs as l
      LEFT OUTER JOIN Reviews as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'review'
      AND (e.[State] != 'PENDING' OR e.UserId = @UserId)

    -- Pull Request Comments
    SELECT l.ItemId as Id, e.IssueId, e.RepositoryId, e.UserId,
      e.PullRequestReviewId, e.DiffHunk, e.[Path], e.Position,
      e.OriginalPosition, e.CommitId, e.OriginalCommitId,
      e.Body, e.CreatedAt, e.UpdatedAt, l.[Delete]
    FROM @Logs as l
      LEFT OUTER JOIN PullRequestComments as e ON (l.ItemId = e.Id)
      LEFT OUTER JOIN Reviews as r ON (r.Id = e.PullRequestReviewId)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'prcomment'
      AND (ISNULL(r.[State], '') != 'PENDING' OR r.UserId = @UserId)

    -- Commit Statuses
    SELECT l.ItemId as Id, e.RepositoryId, e.Reference, e.CreatorId,
      e.[State], e.TargetUrl, e.[Description], e.Context,  e.CreatedAt,
      e.UpdatedAt, l.[Delete]
    FROM @Logs as l
      LEFT OUTER JOIN CommitStatuses as e ON (l.ItemId = e.Id)
    WHERE l.RowNumber BETWEEN @WindowBegin AND @WindowEnd
      AND l.ItemType = 'commitstatus'

    -- Current Versions
    SELECT OwnerType, OwnerId, MAX([RowVersion]) as [RowVersion]
    FROM @OwnerVersions
    WHERE RowNumber BETWEEN @WindowBegin AND @WindowEnd
    GROUP BY OwnerType, OwnerId

    SET @WindowBegin = @WindowBegin + @PageSize
    SET @WindowEnd = @WindowEnd + @PageSize
  END
END
