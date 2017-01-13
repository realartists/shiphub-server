CREATE PROCEDURE [dbo].[SetAccountLinkedRepositories]
  @AccountId BIGINT,
  @RepositoryIds MappingTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM AccountRepositories
    OUTPUT 'user' as ItemType, DELETED.AccountId as ItemId
    FROM AccountRepositories as ar
      LEFT OUTER JOIN @RepositoryIds as rids ON (rids.Item1 = ar.AccountId)
    WHERE ar.AccountId = @AccountId
      AND rids.Item1 IS NULL
    OPTION (FORCE ORDER)

    MERGE INTO AccountRepositories WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT @AccountId as AccountId, Item1 as RepositoryId, Item2 as [Admin]
        FROM @RepositoryIds
    ) as [Source]
    ON [Target].AccountId = [Source].AccountId
      AND [Target].RepositoryId = [Source].RepositoryId
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (AccountId, RepositoryId, [Hidden], [Admin])
      VALUES (AccountId, RepositoryId, 0, [Admin])
    -- Update
    WHEN MATCHED AND [Target].[Admin] != [Source].[Admin] THEN
      UPDATE SET
        [Admin] = [Source].[Admin]
    OUTPUT 'user' as ItemType, INSERTED.AccountId as ItemId
    OPTION (LOOP JOIN, FORCE ORDER);

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
