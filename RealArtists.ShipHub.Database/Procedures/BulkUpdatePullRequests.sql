CREATE PROCEDURE [dbo].[BulkUpdatePullRequests]
  @RepositoryId BIGINT,
  @PullRequests PullRequestTableType READONLY,
  @Labels IssueMappingTableType READONLY,
  @Assignees IssueMappingTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [IssueId] BIGINT NOT NULL
  )

  DECLARE @WorkIssues IssueTableType
  DECLARE @WorkLabels IssueMappingTableType
  DECLARE @WorkAssignees IssueMappingTableType

  -- Procedure TVPs are read only :(
  -- Populate work tables and lookup any missing IDs

  -- PRs/Issues
  INSERT INTO @WorkIssues (Id, UserId, Number, [State], Title, Body, MilestoneId, Locked, CreatedAt, UpdatedAt, ClosedAt, ClosedById, PullRequest, Reactions)
  SELECT i.Id, w.UserId, w.Number, w.[State], w.Title, w.Body, w.MilestoneId, w.Locked, w.CreatedAt, w.UpdatedAt, w.ClosedAt, w.ClosedById, w.PullRequest, w.Reactions
  FROM @PullRequests as w
    INNER LOOP JOIN Issues as i WITH (NOLOCK) ON (i.Number = w.Number AND i.RepositoryId = @RepositoryId)
  OPTION (FORCE ORDER)

  -- Labels
  INSERT INTO @WorkLabels (IssueNumber, IssueId, MappedId)
  SELECT IssueNumber, i.Id, MappedId
  FROM @Labels as w
    INNER JOIN @WorkIssues as i ON (i.Number = w.IssueNumber)

  -- Assignees
  INSERT INTO @WorkAssignees (IssueNumber, IssueId, MappedId)
  SELECT IssueNumber, i.Id, MappedId
  FROM @Assignees as w
    INNER JOIN @WorkIssues as i ON (i.Number = w.IssueNumber)

  BEGIN TRY
    BEGIN TRANSACTION

    -- Update the Issue data and mappings.
    -- Needs to be part of the main transaction to ensure we don't miss any notifications.
    -- TODO: Maybe not terrible to leave it out. Check the implications.
    EXEC BulkUpdateIssues @RepositoryId = @RepositoryId, @Issues = @WorkIssues, @Labels = @WorkLabels, @Assignees = @WorkAssignees

    UPDATE Issues WITH (SERIALIZABLE) SET
      PullRequestId = pr.PullRequestId,
      PullRequestUpdatedAt = pr.UpdatedAt,
      MaintainerCanModify = pr.MaintainerCanModify,
      Mergeable = pr.Mergeable,
      MergeCommitSha = pr.MergeCommitSha,
      Merged = pr.Merged,
      MergedAt = pr.MergedAt,
      MergedById = pr.MergedById,
      BaseJson = pr.BaseJson,
      HeadJson = pr.HeadJson
    OUTPUT INSERTED.Id INTO @Changes
    FROM @PullRequests as pr
      INNER LOOP JOIN Issues as i ON (i.Number = pr.Number AND i.RepositoryId = @RepositoryId)
    WHERE pr.UpdatedAt > ISNULL(i.PullRequestUpdatedAt, '1/1/1970')
    OPTION (FORCE ORDER)

    -- Update existing issues
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    FROM (SELECT DISTINCT IssueId FROM @Changes) as c
      INNER LOOP JOIN SyncLog as sl ON (sl.OwnerType = 'repo' AND sl.OwnerId = @RepositoryId AND sl.ItemType = 'issue' AND sl.ItemId = c.IssueId)
    OPTION (FORCE ORDER)

    -- New Accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
    FROM (
      SELECT DISTINCT(MergedById) as UserId
      FROM Issues as c
          INNER JOIN @Changes as ch ON (c.Id = ch.IssueId)
      WHERE MergedById IS NOT NULL
    ) as c
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog
      WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'account' AND ItemId = c.UserId)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
