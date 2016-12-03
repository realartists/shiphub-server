CREATE PROCEDURE [dbo].[SetRepositoryAssignableAccounts]
  @RepositoryId BIGINT,
  @AssignableAccountIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  MERGE INTO RepositoryAccounts WITH (UPDLOCK SERIALIZABLE) as [Target]
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
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'account', a.Item, 0
  FROM @AssignableAccountIds as a
  WHERE NOT EXISTS (
    SELECT * FROM SyncLog WITH (UPDLOCK)
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'account' AND ItemId = a.Item)

  -- Update repo record in log
  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE)
    SET [RowVersion] = DEFAULT
  OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
  WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'repository' AND ItemId = @RepositoryId
    AND @Changes > 0
END
