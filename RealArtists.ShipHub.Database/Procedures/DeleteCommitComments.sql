CREATE PROCEDURE [dbo].[DeleteCommitComments]
  @Comments ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @DeletedReactions TABLE (
    [ReactionId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM Reactions
    OUTPUT DELETED.Id INTO @DeletedReactions
    FROM @Comments as c
      INNER LOOP JOIN Reactions as r ON (r.CommitCommentId = c.Item)
    OPTION (FORCE ORDER)

    DELETE FROM CommitComments
    FROM @Comments as dc
      INNER LOOP JOIN CommitComments as c ON (c.Id = dc.Item)
    OPTION (FORCE ORDER)

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
      AND ItemId IN (SELECT Item FROM @Comments)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
