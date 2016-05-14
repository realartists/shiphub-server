CREATE PROCEDURE [dbo].[UpdateAccountLinkedRepositories]
  @AccountId INT,
  @RepositoryIds IntListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO AccountRepositories as Target
  USING (
    SELECT @AccountId as AccountId, Item as RepositoryId
      FROM @RepositoryIds) as Source
  ON Target.AccountId = Source.AccountId
    AND Target.RepositoryId = Source.RepositoryId
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (AccountId, RepositoryId, [Hidden]) VALUES (AccountId, RepositoryId, 0)
  WHEN NOT MATCHED BY SOURCE AND Target.AccountId = @AccountId
    THEN DELETE;

  IF(@@ROWCOUNT > 0)
  BEGIN
    -- TODO: This will cause spurious syncing with clients other than the impacted user.
    UPDATE Accounts SET
      [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
    WHERE Id = @AccountId
    RETURN 1
  END
  
  -- ELSE
  RETURN 0
END
