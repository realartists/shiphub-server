CREATE PROCEDURE [dbo].[BulkUpdatePullRequestComments]
  @RepositoryId BIGINT,
  @IssueId BIGINT,
  @PendingReviewId BIGINT = NULL,
  @Comments PullRequestCommentTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [Id]     BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [UserId] BIGINT       NOT NULL,
    [Action] NVARCHAR(10) NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    DECLARE @MatchId BIGINT = ISNULL(@PendingReviewId, -1)
    
    -- Delete is tricky
    -- We never have a complete view of all pending review comments
    DELETE FROM PullRequestComments
    OUTPUT DELETED.Id, DELETED.UserId, 'DELETE' INTO @Changes
    FROM PullRequestComments as prc
      LEFT OUTER JOIN @Comments as prcp ON (prcp.Id = prc.Id)
    WHERE prc.IssueId = @IssueId
      AND ISNULL(prc.PullRequestReviewId, -1) = @MatchId
      AND prcp.Id IS NULL
    OPTION (FORCE ORDER)

    MERGE INTO PullRequestComments WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT c.Id, c.UserId, c.PullRequestReviewId, c.DiffHunk, c.[Path], c.Position, c.OriginalPosition, c.CommitId, c.OriginalCommitId, c.InReplyTo, c.Body, c.CreatedAt, c.UpdatedAt
      FROM @Comments as c
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, IssueId,  RepositoryId,  UserId, PullRequestReviewId, DiffHunk, [Path], Position, OriginalPosition, CommitId, OriginalCommitId, InReplyTo, Body, CreatedAt, UpdatedAt)
      VALUES (Id, @IssueId, @RepositoryId, UserId, PullRequestReviewId, DiffHunk, [Path], Position, OriginalPosition, CommitId, OriginalCommitId, InReplyTo, Body, CreatedAt, UpdatedAt)
    -- Update
    WHEN MATCHED AND [Target].[UpdatedAt] < [Source].[UpdatedAt] THEN
      UPDATE SET
        UserId = [Source].[UserId], -- You'd think this couldn't change, but it can become the Ghost
        PullRequestReviewId = [Source].PullRequestReviewId,
        DiffHunk = [Source].DiffHunk,
        [Path] = [Source].[Path],
        Position = [Source].Position,
        OriginalPosition = [Source].OriginalPosition,
        CommitId = [Source].CommitId,
        OriginalCommitId = [Source].OriginalCommitId,
        InReplyTo = [Source].InReplyTo,
        Body = [Source].Body,
        UpdatedAt = [Source].UpdatedAt
    OUTPUT INSERTED.Id, INSERTED.UserId, $action INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Deleted or edited comments
    UPDATE SyncLog SET
      [Delete] = IIF([Action] = 'DELETE', 1, 0),
      [RowVersion] = DEFAULT
    FROM @Changes as c
      INNER LOOP JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'prcomment' AND ItemId = c.Id)
    OPTION (FORCE ORDER)

    -- New comments
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'prcomment', c.Id, 0
    FROM @Changes as c
    WHERE c.[Action] = 'INSERT'
      AND NOT EXISTS (
        SELECT * FROM SyncLog
        WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'prcomment' AND ItemId = c.Id)

    -- New Accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
    FROM (SELECT DISTINCT UserId FROM @Changes) as c
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
