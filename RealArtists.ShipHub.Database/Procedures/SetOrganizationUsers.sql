CREATE PROCEDURE [dbo].[SetOrganizationUsers]
  @OrganizationId BIGINT,
  @UserIds MappingTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to repo log
  DECLARE @Changes TABLE (
    [UserId] BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [Action] NVARCHAR(10) NOT NULL
  )

  MERGE INTO OrganizationAccounts WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (SELECT Item1 as UserId, Item2 as [Admin] FROM @UserIds) as [Source]
  ON ([Target].UserId = [Source].UserId  AND [Target].OrganizationId = @OrganizationId)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (UserId, OrganizationId, [Admin])
    VALUES (UserId, @OrganizationId, [Admin])
 -- Update
  WHEN MATCHED AND [Target].[Admin] != [Source].[Admin] THEN
    UPDATE SET
      [Admin] = [Source].[Admin]
  -- Delete
  WHEN NOT MATCHED BY SOURCE AND [Target].OrganizationId = @OrganizationId
    THEN DELETE
  OUTPUT COALESCE(INSERTED.UserId, DELETED.UserId), $action INTO @Changes (UserId, [Action]);

  -- Deleted or edited users
  UPDATE OrganizationLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = CAST(CASE WHEN [Action] = 'DELETE' THEN 1 ELSE 0 END as BIT),
    [RowVersion] = DEFAULT
  FROM @Changes 
    INNER JOIN OrganizationLog ON (UserId = AccountId AND OrganizationId = @OrganizationId)

  -- New users
  INSERT INTO OrganizationLog WITH (UPDLOCK SERIALIZABLE) (OrganizationId, AccountId, [Delete])
  SELECT @OrganizationId, c.UserId, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT * FROM OrganizationLog WHERE AccountId = UserId AND OrganizationId = @OrganizationId)

  -- Return updated organizations and users
  SELECT @OrganizationId as OrganizationId, NULL as RepositoryId, UserId
  FROM @Changes
END
