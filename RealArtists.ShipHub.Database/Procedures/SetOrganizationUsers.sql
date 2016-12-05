CREATE PROCEDURE [dbo].[SetOrganizationUsers]
  @OrganizationId BIGINT,
  @UserIds MappingTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- When one or more organization members have valid tokens,
  -- this is the primary way organization membership and
  -- permissions are tracked.

  -- However, when the first user of an organization joins,
  -- or the last member leaves, we count on SetUserOrganizations
  -- to bootstrap (add first) or clean up (remove last).

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [UserId] BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [Action] NVARCHAR(10) NOT NULL
  )

  MERGE INTO OrganizationAccounts WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT Item1 as UserId, Item2 as [Admin] FROM @UserIds
  ) as [Source]
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
  OUTPUT COALESCE(INSERTED.UserId, DELETED.UserId), $action INTO @Changes;

  -- New Accounts
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'org', @OrganizationId, 'account', c.UserId, 0
  FROM @Changes as c
  WHERE c.[Action] = 'INSERT'
    AND NOT EXISTS (
      SELECT * FROM SyncLog WITH (UPDLOCK)
      WHERE OwnerType = 'org' AND OwnerId = @OrganizationId AND ItemType = 'account' AND ItemId = c.UserId)

  -- If users added or removed, bump the org
  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [RowVersion] = DEFAULT
  -- Crafty change output
  OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
  WHERE OwnerType = 'org'
    AND OwnerId = @OrganizationId
    AND ItemType = 'account'
    AND ItemId = @OrganizationId
    AND EXISTS (SELECT * FROM @Changes)
END
