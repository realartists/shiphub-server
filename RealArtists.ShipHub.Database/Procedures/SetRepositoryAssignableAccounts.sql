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

    DECLARE @Changes INT = 0

    DELETE FROM RepositoryAccounts
    FROM RepositoryAccounts as ra
      LEFT OUTER JOIN @AssignableAccountIds as aids ON (aids.Item = ra.AccountId)
    WHERE ra.RepositoryId = @RepositoryId
      AND aids.Item IS NULL
    OPTION (FORCE ORDER)

    SET @Changes = @Changes + @@ROWCOUNT

    MERGE INTO RepositoryAccounts WITH (SERIALIZABLE) as [Target]
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
    OPTION (LOOP JOIN, FORCE ORDER);

    SET @Changes = @Changes + @@ROWCOUNT

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
