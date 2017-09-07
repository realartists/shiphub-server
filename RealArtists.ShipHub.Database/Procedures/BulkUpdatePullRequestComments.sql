CREATE PROCEDURE [dbo].[BulkUpdatePullRequestComments]
  @RepositoryId BIGINT,
  @IssueId BIGINT,
  @PendingReviewId BIGINT = NULL,
  @DropWithMissingReview BIT = 0,
  @Comments PullRequestCommentTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For planning and foreign key satisfaction
  DECLARE @DeletedComments TABLE (
    [CommentId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  DECLARE @DeletedReactions TABLE (
    [ReactionId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [Id]     BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [UserId] BIGINT       NOT NULL,
    [Action] NVARCHAR(10) NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    DECLARE @MatchId BIGINT = ISNULL(@PendingReviewId, -1)

    DECLARE @WorkComments PullRequestCommentTableType

    INSERT INTO @WorkComments (Id, UserId, PullRequestReviewId, DiffHunk, [Path], Position, OriginalPosition, CommitId, OriginalCommitId, Body, CreatedAt, UpdatedAt)
    SELECT c.Id, c.UserId, c.PullRequestReviewId, c.DiffHunk, c.[Path], c.Position, c.OriginalPosition, c.CommitId, c.OriginalCommitId, c.Body, c.CreatedAt, c.UpdatedAt
    FROM @Comments as c
      LEFT OUTER JOIN Reviews as r ON (r.Id = c.PullRequestReviewId)
    WHERE @DropWithMissingReview = 0
      OR c.PullRequestReviewId IS NULL -- No match expected
      OR r.Id IS NOT NULL -- Match expected and found

    -- Delete is tricky
    -- Compute comments to delete
    -- We never have a complete view of all pending review comments
    INSERT INTO @DeletedComments (CommentId)
    SELECT prc.Id
    FROM PullRequestComments as prc
      LEFT OUTER JOIN @WorkComments as prcp ON (prcp.Id = prc.Id)
    WHERE prc.IssueId = @IssueId
      AND ISNULL(prc.PullRequestReviewId, -1) = @MatchId
      AND prcp.Id IS NULL
    OPTION (FORCE ORDER)

    -- Reactions first
    DELETE FROM Reactions
    OUTPUT DELETED.Id INTO @DeletedReactions
    FROM @DeletedComments as dc
      INNER LOOP JOIN Reactions as r ON (r.PullRequestCommentId = dc.CommentId)
    OPTION (FORCE ORDER)

    -- Now deleted comments
    DELETE FROM PullRequestComments
    OUTPUT DELETED.Id, DELETED.UserId, 'DELETE' INTO @Changes
    FROM @DeletedComments as dc
      INNER LOOP JOIN  PullRequestComments as prc ON (prc.Id = dc.CommentId)
    OPTION (FORCE ORDER)

    MERGE INTO PullRequestComments WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT c.Id, c.UserId, c.PullRequestReviewId, c.DiffHunk, c.[Path], c.Position, c.OriginalPosition, c.CommitId, c.OriginalCommitId, c.Body, c.CreatedAt, c.UpdatedAt
      FROM @WorkComments as c
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, IssueId,  RepositoryId,  UserId, PullRequestReviewId, DiffHunk, [Path], Position, OriginalPosition, CommitId, OriginalCommitId, Body, CreatedAt, UpdatedAt)
      VALUES (Id, @IssueId, @RepositoryId, UserId, PullRequestReviewId, DiffHunk, [Path], Position, OriginalPosition, CommitId, OriginalCommitId, Body, CreatedAt, UpdatedAt)
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
        Body = [Source].Body,
        UpdatedAt = [Source].UpdatedAt
    OUTPUT INSERTED.Id, INSERTED.UserId, $action INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Deleted reactions
    UPDATE SyncLog SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    FROM @DeletedReactions as c
      INNER LOOP JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'reaction' AND ItemId = c.ReactionId)
    OPTION (FORCE ORDER)

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
