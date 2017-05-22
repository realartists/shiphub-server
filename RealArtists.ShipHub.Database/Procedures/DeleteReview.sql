CREATE PROCEDURE [dbo].[DeleteReview]
  @ReviewId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @DeletedComments TABLE (
    [CommentId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  DECLARE @DeletedReactions TABLE (
    [ReactionId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  BEGIN TRY
    BEGIN TRANSACTION

    -- Delete reactions on review comments
    -- I don't think this is currently possible but let's play safe
    DELETE FROM Reactions
    OUTPUT DELETED.Id INTO @DeletedReactions
    FROM PullRequestComments as prc
      INNER LOOP JOIN Reactions as r ON (r.PullRequestCommentId = prc.Id)
    WHERE prc.PullRequestReviewId = @ReviewId
    OPTION (FORCE ORDER)

    -- Delete comments on deleted review
    DELETE FROM PullRequestComments
    OUTPUT DELETED.Id INTO @DeletedComments
    WHERE PullRequestReviewId = @ReviewId

    -- Delete review
    DELETE FROM Reviews WHERE Id = @ReviewId

    -- Deleted reactions
    UPDATE SyncLog SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    FROM @DeletedReactions as c
      INNER LOOP JOIN SyncLog ON (ItemType = 'reaction' AND ItemId = c.ReactionId)
    OPTION (FORCE ORDER)

    -- Deleted comments
    UPDATE SyncLog SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    FROM @DeletedComments as c
      INNER LOOP JOIN SyncLog ON (ItemType = 'prcomment' AND ItemId = c.CommentId)
    OPTION (FORCE ORDER)

    -- Deleted review
    UPDATE SyncLog SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    WHERE ItemType = 'review' AND ItemId = @ReviewId

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
