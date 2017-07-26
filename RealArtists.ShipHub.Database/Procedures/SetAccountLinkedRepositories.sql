CREATE PROCEDURE [dbo].[SetAccountLinkedRepositories]
  @AccountId BIGINT,
  @Permissions RepositoryPermissionsTableType READONLY
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
      LEFT OUTER JOIN @Permissions as p ON (p.RepositoryId = ar.RepositoryId)
    WHERE ar.AccountId = @AccountId
      AND p.RepositoryId IS NULL
    OPTION (FORCE ORDER)

    MERGE INTO AccountRepositories WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT @AccountId as AccountId, RepositoryId, [Admin], Push, Pull
        FROM @Permissions
    ) as [Source]
    ON [Target].AccountId = [Source].AccountId
      AND [Target].RepositoryId = [Source].RepositoryId
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (AccountId, RepositoryId, [Admin], Push, Pull)
      VALUES (AccountId, RepositoryId, [Admin], Push, Pull)
    -- Update
    WHEN MATCHED AND EXISTS (
        SELECT [Target].[Admin], [Target].Pull, [Target].Push
        EXCEPT
        SELECT [Source].[Admin], [Source].Pull, [Source].Push
      ) THEN
      UPDATE SET
        [Admin] = [Source].[Admin],
        Push = [Source].Push,
        Pull = [Source].Pull
    OUTPUT 'user' as ItemType, INSERTED.AccountId as ItemId
    OPTION (LOOP JOIN, FORCE ORDER);

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
