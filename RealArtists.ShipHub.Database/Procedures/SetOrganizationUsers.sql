CREATE PROCEDURE [dbo].[SetOrganizationUsers]
  @OrganizationId INT,
  @UserIds IntListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO AccountOrganizations as [Target]
  USING (SELECT [Item] as [UserId] FROM @UserIds) as [Source]
  ON ([Target].[UserId] = [Source].[UserId]  AND [Target].[OrganizationId] = @OrganizationId)
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([UserId], [OrganizationId])
    VALUES ([UserId], @OrganizationId)
  WHEN NOT MATCHED BY SOURCE AND [Target].[OrganizationId] = @OrganizationId
    THEN DELETE;

  IF(@@ROWCOUNT > 0)
  BEGIN
    -- TODO: This will cause spurious syncing with clients other than the impacted user.
    UPDATE Accounts SET
      [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
    WHERE Id IN (
      SELECT @OrganizationId
      UNION
      SELECT [Item] FROM @UserIds)

    RETURN 1
  END
  
  -- ELSE
  RETURN 0
END
