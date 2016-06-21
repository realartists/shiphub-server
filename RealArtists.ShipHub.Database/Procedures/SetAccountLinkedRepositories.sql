CREATE PROCEDURE [dbo].[SetAccountLinkedRepositories]
  @AccountId BIGINT,
  @RepositoryIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO AccountRepositories WITH (SERIALIZABLE) as Target
  USING (
    SELECT @AccountId as AccountId, Item as RepositoryId
      FROM @RepositoryIds) as Source
  ON Target.AccountId = Source.AccountId
    AND Target.RepositoryId = Source.RepositoryId
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (AccountId, RepositoryId, [Hidden])
    VALUES (AccountId, RepositoryId, 0)
  -- Remove
  WHEN NOT MATCHED BY SOURCE AND Target.AccountId = @AccountId
    THEN DELETE
  OPTION (RECOMPILE);
END
