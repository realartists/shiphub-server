CREATE PROCEDURE [dbo].[SetOrganizationUsers]
  @OrganizationId BIGINT,
  @UserIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO AccountOrganizations WITH (SERIALIZABLE) as [Target]
  USING (SELECT [Item] as [UserId] FROM @UserIds) as [Source]
  ON ([Target].[UserId] = [Source].[UserId]  AND [Target].[OrganizationId] = @OrganizationId)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([UserId], [OrganizationId])
    VALUES ([UserId], @OrganizationId)
  -- Delete
  WHEN NOT MATCHED BY SOURCE AND [Target].[OrganizationId] = @OrganizationId
    THEN DELETE;
END
