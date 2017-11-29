CREATE PROCEDURE [dbo].[BulkUpdateHooks]
  @Hooks HookTableType READONLY,
  @Seen ItemListTableType READONLY,
  @Deleted ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

-- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    -- Not directly used but forces ordering for scans
    [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [RepositoryId] BIGINT NULL,
    [OrganizationId] BIGINT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO Hooks WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Id, GitHubId, [Secret], [Events], [LastError]
      FROM @Hooks
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Update
    WHEN MATCHED THEN UPDATE SET
      GitHubId = [Source].GitHubId,
      [Secret] = [Source].[Secret],
      [Events] = [Source].[Events],
      [LastError] = [Source].[LastError]
    OUTPUT INSERTED.Id, INSERTED.RepositoryId, INSERTED.OrganizationId INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Deleted hooks
    DELETE FROM Hooks
    OUTPUT DELETED.Id, DELETED.RepositoryId, DELETED.OrganizationId INTO @Changes
    FROM @Deleted as d
      INNER LOOP JOIN Hooks as h ON (h.Id = d.Item)
    OPTION (FORCE ORDER)

    -- Seen hooks
    UPDATE Hooks SET
      LastSeen = SYSUTCDATETIME()
    FROM @Seen as s
      INNER LOOP JOIN Hooks as h ON (h.Id = s.Item)
    OPTION (FORCE ORDER)

    -- Changes
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT -- Bump version
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    FROM @Changes as c
      INNER LOOP JOIN SyncLog as sl ON (sl.OwnerType = 'repo' AND sl.OwnerId = c.RepositoryId AND sl.ItemType = 'repository' AND sl.ItemId = c.RepositoryId)
    OPTION (FORCE ORDER)

    UPDATE SyncLog SET
      [RowVersion] = DEFAULT -- Bump version
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    FROM @Changes as c
      INNER LOOP JOIN SyncLog as sl ON (sl.OwnerType = 'org' AND sl.OwnerId = c.OrganizationId AND sl.ItemType = 'account' AND sl.ItemId = c.OrganizationId)
    OPTION (FORCE ORDER)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
