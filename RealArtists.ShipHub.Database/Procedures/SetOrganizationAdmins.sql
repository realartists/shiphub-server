CREATE PROCEDURE [dbo].[SetOrganizationAdmins]
  @OrganizationId BIGINT,
  @AdminIds ItemListTableType READONLY
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

    -- Unmark any extra admins
    UPDATE OrganizationAccounts
      SET [Admin] = 0
    OUTPUT INSERTED.UserId, 'INSERT' INTO @Changes
    FROM OrganizationAccounts as oa
      LEFT OUTER JOIN @AdminIds as uids ON (uids.Item = oa.UserId)
    WHERE oa.OrganizationId = @OrganizationId
      AND oa.[Admin] = 1
      AND uids.Item IS NULL
    OPTION (FORCE ORDER)

    MERGE INTO OrganizationAccounts WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Item as UserId FROM @AdminIds
    ) as [Source]
    ON ([Target].OrganizationId = @OrganizationId AND [Target].UserId = [Source].UserId)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (UserId, OrganizationId, [Admin])
      VALUES (UserId, @OrganizationId, 1)
   -- Update
    WHEN MATCHED AND [Target].[Admin] != 1 THEN
      UPDATE SET
        [Admin] = 1
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
