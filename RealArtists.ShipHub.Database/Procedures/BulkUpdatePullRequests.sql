CREATE PROCEDURE [dbo].[BulkUpdatePullRequests]
  @RepositoryId BIGINT,
  @PullRequests PullRequestTableType READONLY,
  @Reviewers IssueMappingTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- Work tables
  DECLARE @WorkPullRequests PullRequestTableType

  INSERT INTO @WorkPullRequests (
         Id, IssueId,  Number,   CreatedAt,   UpdatedAt, MergeCommitSha, MergedAt, BaseJson, HeadJson, Additions, ChangedFiles, Commits, Deletions, MaintainerCanModify, Mergeable, MergeableState, MergedById, Rebaseable, [Hash])
  SELECT p.Id, i.Id, p.Number, p.CreatedAt, p.UpdatedAt, MergeCommitSha, MergedAt, BaseJson, HeadJson, Additions, ChangedFiles, Commits, Deletions, MaintainerCanModify, Mergeable, MergeableState, MergedById, Rebaseable, [Hash]
  FROM @PullRequests as p
    INNER JOIN Issues as i ON (i.Number = p.Number AND i.RepositoryId = @RepositoryId)

  DECLARE @WorkReviewers IssueMappingTableType

  INSERT INTO @WorkReviewers (IssueNumber, IssueId, MappedId)
  SELECT r.IssueNumber, p.IssueId, r.MappedId
  FROM @Reviewers as r
    INNER JOIN @WorkPullRequests as p ON (p.Number = r.IssueNumber)

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [IssueId] BIGINT NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO PullRequests WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Id, IssueId, Number, CreatedAt, UpdatedAt, MergeCommitSha, MergedAt, BaseJson, HeadJson, Additions, ChangedFiles, Commits, Deletions, MaintainerCanModify, Mergeable, MergeableState, MergedById, Rebaseable, [Hash]
      FROM @WorkPullRequests
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, IssueId, RepositoryId,  Number, CreatedAt, UpdatedAt, MergeCommitSha, MergedAt, BaseJson, HeadJson, Additions, ChangedFiles, Commits, Deletions, MaintainerCanModify, Mergeable, MergeableState, MergedById, Rebaseable, [Hash])
      VALUES (Id, IssueId, @RepositoryId, Number, CreatedAt, UpdatedAt, MergeCommitSha, MergedAt, BaseJson, HeadJson, Additions, ChangedFiles, Commits, Deletions, MaintainerCanModify, Mergeable, MergeableState, MergedById, Rebaseable, [Hash])
    -- Update (this bumps for label only changes too)
    WHEN MATCHED AND (
        [Source].UpdatedAt > ISNULL([Target].UpdatedAt, '1/1/1970')
        OR (
          [Source].UpdatedAt = ISNULL([Target].UpdatedAt, '1/1/1970')
          AND [Source].[Hash] IS NOT NULL
          AND ISNULL([Source].[Hash], '00000000-0000-0000-0000-000000000000') != ISNULL([Target].[Hash], '00000000-0000-0000-0000-000000000000'))
      ) THEN
      UPDATE SET
        UpdatedAt = [Source].UpdatedAt,
        MergeCommitSha = [Source].MergeCommitSha,
        MergedAt = ISNULL([Source].MergedAt, [Target].MergedAt),
        BaseJson = [Source].BaseJson,
        HeadJson = [Source].HeadJson,
        Additions = ISNULL([Source].Additions, [Target].Additions),
        ChangedFiles = ISNULL([Source].ChangedFiles, [Target].ChangedFiles),
        Commits = ISNULL([Source].Commits, [Target].Commits),
        Deletions = ISNULL([Source].Deletions, [Target].Deletions),
        MaintainerCanModify = ISNULL([Source].MaintainerCanModify, [Target].MaintainerCanModify),
        Mergeable = ISNULL([Source].Mergeable, [Target].Mergeable),
        MergeableState = ISNULL([Source].MergeableState, [Target].MergeableState),
        MergedById = ISNULL([Source].MergedById, [Target].MergedById),
        Rebaseable = ISNULL([Source].Rebaseable, [Target].Rebaseable),
        [Hash] = ISNULL([Source].[Hash], [Target].[Hash])
    OUTPUT INSERTED.IssueId INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Reviewers
    -- Have to use the same tricks as above for the same reasons.
    MERGE INTO PullRequestReviewers WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT r.IssueId, r.MappedId as UserId
      FROM @WorkReviewers as r
    ) as [Source]
    ON ([Target].IssueId = [Source].IssueId AND [Target].UserId = [Source].UserId)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (IssueId, UserId)
      VALUES (IssueId, UserId)
    OUTPUT INSERTED.IssueId INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Delete any extraneous mappings.
    -- Same tricks, same justification
    DELETE FROM PullRequestReviewers
    OUTPUT DELETED.IssueId INTO @Changes
    FROM @WorkPullRequests as pr
      INNER LOOP JOIN PullRequestReviewers as prr ON (prr.IssueId = pr.IssueId)
      LEFT OUTER JOIN @WorkReviewers as wr ON (wr.IssueId = prr.IssueId AND wr.MappedId = prr.UserId)
    WHERE wr.IssueId IS NULL
    OPTION (FORCE ORDER)

    -- Update existing issues (PR requires an exsiting issue)
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
      FROM @WorkPullRequests as pr
        INNER JOIN @Changes as c ON (pr.IssueId = c.IssueId)
      WHERE MergedById IS NOT NULL
      UNION
      SELECT MappedId
      FROM @WorkReviewers as wr
        INNER JOIN @Changes as c ON (wr.IssueId = c.IssueId)
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
