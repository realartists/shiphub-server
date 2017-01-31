CREATE PROCEDURE [dbo].[SetUserOrganizations]
  @UserId BIGINT,
  @OrganizationIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- This stored proc ensures that the first member of an organization
  -- is added, and that the last member is removed, despite having no
  -- organization members with valid tokens at either time.
  
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

    MERGE INTO OrganizationAccounts WITH (SERIALIZABLE) as [Target]
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

    -- Organizations reference themselves
    MERGE INTO SyncLog WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Item as OwnerId, Item as ItemId
      FROM @OrganizationIds
    ) as [Source]
    ON (  [Target].OwnerType = 'org' AND [Target].OwnerId = [Source].OwnerId
      AND [Target].ItemType = 'account' AND [Target].ItemId = [Source].ItemId)
    WHEN NOT MATCHED BY TARGET THEN
      INSERT(OwnerType, OwnerId, ItemType, ItemId, [Delete])
      VALUES('org', OwnerId, 'account', ItemId, 0)
    OPTION (LOOP JOIN, FORCE ORDER);

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
