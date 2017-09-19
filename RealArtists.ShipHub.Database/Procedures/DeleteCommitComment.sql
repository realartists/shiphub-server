CREATE PROCEDURE [dbo].[DeleteCommitComment]
  @CommentId BIGINT,
  @Date DATETIMEOFFSET NULL
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @DeletedReactions TABLE (
    [ReactionId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  -- GitHub may return 404s for newly created comments. Good times.
  -- Don't delete unless at least some time has passed.
  IF (@Date IS NOT NULL
    AND NOT EXISTS(SELECT * FROM CommitComments WHERE Id = @CommentId AND DATEDIFF(SECOND, CreatedAt, @Date) > 10))
  BEGIN
    RETURN
  END

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM Reactions
    OUTPUT DELETED.Id INTO @DeletedReactions
    WHERE CommitCommentId = @CommentId

    DELETE FROM CommitComments WHERE Id = @CommentId

    -- Deleted reactions
    UPDATE SyncLog SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    WHERE ItemType = 'reaction'
      AND [Delete] = 0
      AND ItemId IN (SELECT ReactionId FROM @DeletedReactions)

    UPDATE SyncLog SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    -- Crafty change output
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    WHERE ItemType = 'commitcomment'
      AND [Delete] = 0
      AND ItemId = @CommentId

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
