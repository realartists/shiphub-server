CREATE PROCEDURE [dbo].[SetRepositoryAssignableAccounts]
  @RepositoryId BIGINT,
  @AssignableAccountIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO RepositoryAccounts WITH (SERIALIZABLE) as Target
  USING (
    SELECT Item as AccountId, @RepositoryId as RepositoryId
      FROM @AssignableAccountIds) as Source
  ON Target.AccountId = Source.AccountId
    AND Target.RepositoryId = Source.RepositoryId
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (AccountId, RepositoryId)
    VALUES (AccountId, RepositoryId)
  -- Delete
  WHEN NOT MATCHED BY SOURCE
    AND Target.RepositoryId = @RepositoryId
    THEN DELETE;

   IF(@@ROWCOUNT > 0) -- Not a NOP
   BEGIN
    -- Update repo record in log
    UPDATE RepositoryLog
      SET [RowVersion] = NULL
    WHERE RepositoryId = @RepositoryId
      AND [Type] = 'repository'
      -- AND ItemId = @RepositoryId

    -- Add any missing accounts to the log
    MERGE INTO RepositoryLog WITH (SERIALIZABLE) as [Target]
    USING (SELECT Item as AccountId FROM @AssignableAccountIds) as [Source]
    ON ([Target].RepositoryId = @RepositoryId
      AND [Target].[Type] = 'account'
      AND [Target].ItemId = AccountId)
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (RepositoryId, [Type], ItemId, [Delete])
      VALUES (@RepositoryId, 'account', AccountId, 0);
  END
END
