CREATE PROCEDURE [dbo].[DisableRepository]
  @RepositoryId BIGINT,
  @Disabled BIT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    UPDATE Repositories SET
      [Disabled] = @Disabled
    WHERE Id = @RepositoryId AND [Disabled] != @Disabled

    IF(@@ROWCOUNT > 0)
    BEGIN
      UPDATE SyncLog SET
        [RowVersion] = DEFAULT
      -- Crafty change output
      OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
      WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'repo' AND ItemId = @RepositoryId
    END

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
