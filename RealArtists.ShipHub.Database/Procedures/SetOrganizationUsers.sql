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

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM OrganizationAccounts
    OUTPUT DELETED.UserId, 'DELETE' INTO @Changes
    FROM OrganizationAccounts as oa
      LEFT OUTER JOIN @UserIds as uids ON (uids.Item1 = oa.UserId)
    WHERE oa.OrganizationId = @OrganizationId
      AND uids.Item1 IS NULL
    OPTION (FORCE ORDER)

    MERGE INTO OrganizationAccounts as [Target]
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
    OUTPUT INSERTED.UserId, $action INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- New Accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'org', @OrganizationId, 'account', c.UserId, 0
    FROM @Changes as c
    WHERE c.[Action] = 'INSERT'
      AND NOT EXISTS (
        SELECT * FROM SyncLog
        WHERE OwnerType = 'org' AND OwnerId = @OrganizationId AND ItemType = 'account' AND ItemId = c.UserId)

    -- If users added or removed, bump the org
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    -- Crafty change output
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    WHERE OwnerType = 'org'
      AND OwnerId = @OrganizationId
      AND ItemType = 'account'
      AND ItemId = @OrganizationId
      AND EXISTS (SELECT * FROM @Changes)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
