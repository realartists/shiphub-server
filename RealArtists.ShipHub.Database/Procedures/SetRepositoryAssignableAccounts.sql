CREATE PROCEDURE [dbo].[SetRepositoryAssignableAccounts]
  @RepositoryId BIGINT,
  @AssignableAccountIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes BIT = 0

  MERGE INTO RepositoryAccounts WITH (UPDLOCK SERIALIZABLE) as Target
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
    SET @Changes = 1

    -- Update repo record in log
    UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE)
      SET [RowVersion] = DEFAULT
    WHERE RepositoryId = @RepositoryId
      AND [Type] = 'repository'
      -- AND ItemId = @RepositoryId

    -- Add any missing accounts to the log
    MERGE INTO RepositoryLog WITH (UPDLOCK SERIALIZABLE) as [Target]
    USING (SELECT Item as AccountId FROM @AssignableAccountIds) as [Source]
    ON ([Target].RepositoryId = @RepositoryId
      AND [Target].[Type] = 'account'
      AND [Target].ItemId = AccountId)
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (RepositoryId, [Type], ItemId, [Delete])
      VALUES (@RepositoryId, 'account', AccountId, 0);
  END

  -- Return updated organizations and repositories
  SELECT NULL as OrganizationId, @RepositoryId as RepositoryId, NULL as UserId WHERE @Changes = 1
END
