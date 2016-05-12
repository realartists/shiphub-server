CREATE PROCEDURE [dbo].[UpdateRepositoryAssignableAccounts]
  @RepositoryId INT,
  @AssignableAccounts AccountStubTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  EXEC [dbo].[StubAccounts] @AccountStubs = @AssignableAccounts

  MERGE INTO AccountRepositories as Target
  USING (
    SELECT AccountId, @RepositoryId as RepositoryId
      FROM @AssignableAccounts) as Source
  ON Target.AccountId = Source.AccountId
    AND Target.RepositoryId = Source.RepositoryId
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (AccountId, RepositoryId) VALUES (AccountId, RepositoryId)
  WHEN NOT MATCHED BY SOURCE THEN DELETE;

  IF(@@ROWCOUNT > 0)
  BEGIN
    UPDATE Repositories SET
      [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
    WHERE Id = @RepositoryId
    RETURN 1
  END
  
  -- ELSE
  RETURN 0
END
