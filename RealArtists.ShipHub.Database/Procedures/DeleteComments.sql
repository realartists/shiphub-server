CREATE PROCEDURE [dbo].[DeleteComments]
  @Comments ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM Reactions
    FROM @Comments as c
      INNER LOOP JOIN Reactions as r ON (r.CommentId = c.Item)
    OPTION (FORCE ORDER)

    DELETE FROM Comments
    FROM @Comments as dc
      INNER LOOP JOIN Comments as c ON (c.Id = dc.Item)
    OPTION (FORCE ORDER)

    UPDATE SyncLog SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    -- Crafty change output
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    WHERE ItemType = 'comment'
      AND [Delete] = 0
      AND ItemId IN (SELECT Item FROM @Comments)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
