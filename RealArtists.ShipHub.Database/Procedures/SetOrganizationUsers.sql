CREATE PROCEDURE [dbo].[SetOrganizationUsers]
  @OrganizationId BIGINT,
  @UserIds ItemListTableType READONLY
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

  MERGE INTO AccountOrganizations WITH (SERIALIZABLE) as [Target]
  USING (SELECT Item as UserId FROM @UserIds) as [Source]
  ON ([Target].UserId = [Source].UserId  AND [Target].OrganizationId = @OrganizationId)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (UserId, OrganizationId)
    VALUES (UserId, @OrganizationId)
  -- Delete
  WHEN NOT MATCHED BY SOURCE AND [Target].OrganizationId = @OrganizationId
    THEN DELETE
  OUTPUT COALESCE(INSERTED.UserId, DELETED.UserId), $action INTO @Changes (UserId, [Action])
  OPTION (RECOMPILE);

  -- Deleted or edited users
  UPDATE OrganizationLog WITH (SERIALIZABLE) SET
    [Delete] = CAST(CASE WHEN [Action] = 'DELETE' THEN 1 ELSE 0 END as BIT),
    [RowVersion] = DEFAULT
  FROM @Changes 
    INNER JOIN OrganizationLog ON (UserId = AccountId AND OrganizationId = @OrganizationId)
  OPTION (RECOMPILE)

  -- New users
  INSERT INTO OrganizationLog WITH (SERIALIZABLE) (OrganizationId, AccountId, [Delete])
  SELECT @OrganizationId, c.UserId, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT 1 FROM OrganizationLog WHERE AccountId = UserId AND OrganizationId = @OrganizationId)
  OPTION (RECOMPILE)

  -- Return updated organizations and repositories
  SELECT @OrganizationId as OrganizationId, NULL as RepositoryId
  WHERE EXISTS(SELECT 1 FROM @Changes)
  OPTION (RECOMPILE)
END
