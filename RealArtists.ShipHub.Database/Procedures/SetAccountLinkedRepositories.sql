CREATE PROCEDURE [dbo].[SetAccountLinkedRepositories]
  @AccountId BIGINT,
  @RepositoryIds MappingTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes BIT = 0

  MERGE INTO AccountRepositories WITH (UPDLOCK SERIALIZABLE) as Target
  USING (
    SELECT @AccountId as AccountId, Item1 as RepositoryId, Item2 as Admin
      FROM @RepositoryIds) as Source
  ON Target.AccountId = Source.AccountId
    AND Target.RepositoryId = Source.RepositoryId
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (AccountId, RepositoryId, [Hidden], [Admin])
    VALUES (AccountId, RepositoryId, 0, [Admin])
  -- Update
  WHEN MATCHED AND [Target].[Admin] != [Source].[Admin] THEN
    UPDATE SET
      [Admin] = [Source].[Admin]
  -- Remove
  WHEN NOT MATCHED BY SOURCE AND Target.AccountId = @AccountId
    THEN DELETE;

  IF(@@ROWCOUNT > 0) -- Not a NOP
  BEGIN
    SET @Changes = 1
  END

  -- Return updated user
  SELECT NULL as OrganizationId, NULL as RepositoryId, @AccountId as UserId WHERE @Changes = 1
END
