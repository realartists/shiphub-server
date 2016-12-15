CREATE PROCEDURE [dbo].[SetRepositoryAssignableAccounts]
  @RepositoryId BIGINT,
  @AssignableAccountIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO RepositoryAccounts as [Target]
    USING (
      SELECT Item as AccountId, @RepositoryId as RepositoryId
      FROM @AssignableAccountIds
    ) as [Source]
    ON [Target].AccountId = [Source].AccountId
      AND [Target].RepositoryId = [Source].RepositoryId
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (AccountId, RepositoryId)
      VALUES (AccountId, RepositoryId)
    -- Delete
    WHEN NOT MATCHED BY SOURCE
      AND [Target].RepositoryId = @RepositoryId
      THEN DELETE;

    DECLARE @Changes INT = @@ROWCOUNT

    -- New Accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'account', a.Item, 0
    FROM @AssignableAccountIds as a
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog
      WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'account' AND ItemId = a.Item)

    -- Update repo record in log
    UPDATE SyncLog
      SET [RowVersion] = DEFAULT
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'repository' AND ItemId = @RepositoryId
      AND @Changes > 0

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
