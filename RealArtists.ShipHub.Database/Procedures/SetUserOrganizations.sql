CREATE PROCEDURE [dbo].[SetUserOrganizations]
  @UserId BIGINT,
  @OrganizationIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- This may appear redundant when compared to SetOrganizationUsers
  -- That's not the case. SetOrganizationUsers is only called by the
  -- OrganizationActor, which only works when there's at least one
  -- organization member with a valid token.

  -- This stored proc ensures that the first member of an organization
  -- is added, and that the last member is removed, despite having no
  -- organization members with valid tokens at either time.

  -- Adding the initial member *SHOULD NOT* trigger a sync, since
  -- SetOrganizationUsers will take care of it.
  
  -- Any removed member *MUST* trigger a sync, since there may
  -- no longer be any valid tokens for the OrganizationActor to use.

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [OrganizationId] BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [Action]         NVARCHAR(10) NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM OrganizationAccounts
    OUTPUT DELETED.OrganizationId, 'DELETE' INTO @Changes
    FROM OrganizationAccounts as oa
      LEFT OUTER JOIN @OrganizationIds as oids ON (oids.Item = oa.OrganizationId)
    WHERE oa.UserId = @UserId
      AND oids.Item IS NULL
    OPTION (FORCE ORDER)

    MERGE INTO OrganizationAccounts as [Target]
    USING (
      SELECT Item as OrganizationId FROM @OrganizationIds
    ) as [Source]
    ON ([Target].OrganizationId = [Source].OrganizationId AND [Target].UserId = @UserId)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (UserId, OrganizationId, [Admin])
      VALUES (@UserId, OrganizationId, 0) -- Default to admin = false, will update if wrong.
    OUTPUT INSERTED.OrganizationId, $action INTO @Changes
    OPTION(LOOP JOIN, FORCE ORDER);

    -- New organizations reference themselves
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'org', c.OrganizationId, 'account', c.OrganizationId, 0
    FROM @Changes as c
    WHERE c.[Action] = 'INSERT'
      AND NOT EXISTS (
        SELECT * FROM SyncLog
        WHERE OwnerType = 'org' AND OwnerId = c.OrganizationId AND ItemType = 'account' AND ItemId = c.OrganizationId)

    -- For new members, add a reference but don't notify.
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'org', c.OrganizationId, 'account', @UserId, 0
    FROM @Changes as c
    WHERE c.[Action] = 'INSERT'
      AND NOT EXISTS (
        SELECT * FROM SyncLog
        WHERE OwnerType = 'org' AND OwnerId = c.OrganizationId AND ItemType = 'account' AND ItemId = @UserId)
    
    -- If user deleted from the org, bump the version and trigger org sync
    -- The LOOP JOIN and FORCE ORDER prevent a scan and merge which deadlocks on PK_SyncLog
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    -- Notify orgs with deletions
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    FROM @Changes as c
      INNER LOOP JOIN SyncLog ON (OwnerType = 'org' AND OwnerId = c.OrganizationId AND ItemType = 'account' AND ItemId = c.OrganizationId)
    WHERE c.[Action] = 'DELETE'
    OPTION (FORCE ORDER)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Notify user
  SELECT 'user' as ItemType, @UserId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
