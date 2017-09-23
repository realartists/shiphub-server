CREATE PROCEDURE [dbo].[DeleteProtectedBranch]
  @RepositoryId BIGINT,
  @Name NVARCHAR(MAX)
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes TABLE (
    [Id] BIGINT NOT NULL PRIMARY KEY
  )

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM ProtectedBranches
    OUTPUT Deleted.Id INTO @Changes
     WHERE RepositoryId = @RepositoryId AND [Name] = @Name

    UPDATE SyncLog SET 
      [RowVersion] = Default,
      [Delete] = 1
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'protectedbranch' AND  ItemId IN (SELECT Id FROM @Changes)
    
    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
