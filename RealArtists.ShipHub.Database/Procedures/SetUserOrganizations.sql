CREATE PROCEDURE [dbo].[SetUserOrganizations]
  @UserId BIGINT,
  @OrganizationIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to repo log
  DECLARE @Changes TABLE (
    [OrganizationId] BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [Action]         NVARCHAR(10) NOT NULL
  )

  MERGE INTO OrganizationAccounts WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (SELECT Item as OrganizationId FROM @OrganizationIds) as [Source]
  ON ([Target].OrganizationId = [Source].OrganizationId AND [Target].UserId = @UserId)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([UserId], [OrganizationId], [Admin])
    VALUES (@UserId, [OrganizationId], 0) -- Safe to default to false, will update if wrong.
  -- Delete
  WHEN NOT MATCHED BY SOURCE AND [Target].[UserId] = @UserId
    THEN DELETE
  OUTPUT COALESCE(INSERTED.OrganizationId, DELETED.OrganizationId), $action INTO @Changes (OrganizationId, [Action]);

  IF (@@ROWCOUNT > 0)
  BEGIN
    -- If user added or removed from orgs, bump the org
    UPDATE OrganizationLog WITH (UPDLOCK SERIALIZABLE) SET
      [RowVersion] = DEFAULT
    FROM @Changes as c
      INNER JOIN OrganizationLog as ol ON (ol.OrganizationId = c.OrganizationId AND ol.AccountId = c.OrganizationId)

    -- New org memberships
    INSERT INTO OrganizationLog WITH (SERIALIZABLE) (OrganizationId, AccountId)
    SELECT c.OrganizationId, @UserId
    FROM @Changes as c
    WHERE c.[Action] = 'INSERT'
      AND NOT EXISTS (SELECT * FROM OrganizationLog WITH (UPDLOCK) WHERE OrganizationId = c.OrganizationId AND AccountId = @UserId)
  END

  -- Return updated organizations and users
  SELECT OrganizationId, NULL as RepositoryId, @UserId as UserId
  FROM @Changes
END
