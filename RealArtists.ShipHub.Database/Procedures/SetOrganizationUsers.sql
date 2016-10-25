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
  ON ([Target].OrganizationId = @OrganizationId AND [Target].UserId = [Source].UserId)
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

  IF (@@ROWCOUNT > 0)
  BEGIN
    -- If users added or removed, bump the org
    UPDATE OrganizationLog WITH (UPDLOCK SERIALIZABLE) SET
      [RowVersion] = DEFAULT
    WHERE OrganizationId = @OrganizationId AND AccountId = @OrganizationId

    -- New users
    INSERT INTO OrganizationLog WITH (SERIALIZABLE) (OrganizationId, AccountId)
    SELECT @OrganizationId, c.UserId
    FROM @Changes as c
    WHERE c.[Action] = 'INSERT'
      AND NOT EXISTS (SELECT * FROM OrganizationLog WITH (UPDLOCK) WHERE OrganizationId = @OrganizationId AND AccountId = UserId)
  END

  -- Return updated organizations and users
  SELECT @OrganizationId as OrganizationId, NULL as RepositoryId, UserId
  FROM @Changes
END
