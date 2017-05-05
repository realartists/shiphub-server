CREATE PROCEDURE [dbo].[BulkUpdateReviews]
  @RepositoryId BIGINT,
  @IssueId BIGINT,
  @Date DATETIMEOFFSET,
  @UserId BIGINT,
  @Reviews ReviewTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @ReviewChanges TABLE (
    [ReviewId] BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [Action]   NVARCHAR(10) NOT NULL
  )

  DECLARE @DeletedComments TABLE (
    [CommentId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  DECLARE @DeletedReactions TABLE (
    [ReactionId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  BEGIN TRY
    BEGIN TRANSACTION

    -- Detect any extraneous reviews
    -- We have to delete any comments referencing it as well
    INSERT INTO @ReviewChanges
    SELECT r.Id, 'DELETE' 
    FROM Reviews as r
      LEFT OUTER JOIN @Reviews as rr ON (rr.Id = r.Id)
    WHERE r.IssueId = @IssueId
      AND rr.Id IS NULL
      -- Right now only pending reviews can be deleted, but let's play it safe.
      -- This should delete any reviews that don't match and aren't pending
      -- plus any that are pending by this user and don't match.
      AND (r.[State] != 'PENDING' OR r.UserId = @UserId) 
    OPTION (FORCE ORDER)

    -- Delete reactions on review comments
    -- I don't think this is currently possible but let's play safe
    DELETE FROM Reactions
    OUTPUT DELETED.Id INTO @DeletedReactions
    FROM @ReviewChanges as rc
      INNER LOOP JOIN PullRequestComments as prc ON (prc.PullRequestReviewId = rc.ReviewId)
      INNER LOOP JOIN Reactions as r ON (r.PullRequestCommentId = prc.Id)
    WHERE rc.[Action] = 'DELETE'
    OPTION (FORCE ORDER)

    -- Delete comments on deleted reviews
    DELETE FROM PullRequestComments
    OUTPUT DELETED.Id INTO @DeletedComments
    FROM @ReviewChanges as rc
      INNER LOOP JOIN PullRequestComments as prc ON (rc.ReviewId = prc.PullRequestReviewId)
    WHERE rc.[Action] = 'DELETE'
    OPTION (FORCE ORDER)

    -- Delete deleted reviews
    DELETE FROM Reviews
    FROM @ReviewChanges as rc
      INNER LOOP JOIN Reviews as r ON (r.Id = rc.ReviewId)
    WHERE rc.[Action] = 'DELETE'
    OPTION (FORCE ORDER)

    MERGE INTO Reviews WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Id, UserId, Body, CommitId, [State], SubmittedAt, [Hash]
      FROM @Reviews
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, IssueId, RepositoryId, UserId, Body, CommitId, [State], SubmittedAt, [Date], [Hash])
      VALUES (Id, @IssueId, @RepositoryId, UserId, Body, CommitId, [State], SubmittedAt, @Date, [Hash])
    WHEN MATCHED AND (
      [Target].[Date] < @Date AND [Source].[Hash] != [Target].[Hash]
    ) THEN UPDATE SET
      Body = [Source].Body,
      CommitId = [Source].CommitId,
      [State] = [Source].[State],
      SubmittedAt = [Source].SubmittedAt,
      [Date] = @Date,
      [Hash] = [Source].[Hash]
    OUTPUT INSERTED.Id, $action INTO @ReviewChanges
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Deleted reactions
    UPDATE SyncLog SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    FROM @DeletedReactions as c
      INNER LOOP JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'reaction' AND ItemId = c.ReactionId)
    OPTION (FORCE ORDER)

    -- Deleted comments
    UPDATE SyncLog SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    FROM @DeletedComments as c
      INNER LOOP JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'prcomment' AND ItemId = c.CommentId)
    OPTION (FORCE ORDER)

    -- Deleted or edited reviews
    UPDATE SyncLog SET
      [Delete] = IIF([Action] = 'DELETE', 1, 0),
      [RowVersion] = DEFAULT
    FROM @ReviewChanges as c
      INNER LOOP JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'review' AND ItemId = c.ReviewId)
    OPTION (FORCE ORDER)

    -- New reviews
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'review', c.ReviewId, 0
    FROM @ReviewChanges as c
    WHERE c.[Action] = 'INSERT'
      AND NOT EXISTS (
        SELECT * FROM SyncLog
        WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'review' AND ItemId = c.ReviewId)

    -- New Accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
    FROM (SELECT DISTINCT UserId FROM @Reviews) as c
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
  WHERE EXISTS (SELECT * FROM @ReviewChanges)
END
