CREATE PROCEDURE [dbo].[BulkUpdateCommitStatuses]
  @RepositoryId BIGINT,
  @Reference NVARCHAR(MAX),
  @Statuses CommitStatusTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [Id]        BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO CommitStatuses WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Id, [State], TargetUrl, [Description], Context, CreatedAt, UpdatedAt
      FROM @Statuses
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, RepositoryId,  Reference, [State], TargetUrl, [Description], Context, CreatedAt, UpdatedAt)
      VALUES (Id, @RepositoryId, @Reference, [State], TargetUrl, [Description], Context, CreatedAt, UpdatedAt)
    -- Update
    WHEN MATCHED AND [Target].[UpdatedAt] < [Source].[UpdatedAt] THEN
      UPDATE SET
        Reference = @Reference,         -- I don't think this can actually change
        [State] = [Source].[State],
        TargetUrl = [Source].TargetUrl,
        [Description] = [Source].[Description],
        Context = [Source].Context,
        CreatedAt = [Source].CreatedAt,
        UpdatedAt = [Source].UpdatedAt
    OUTPUT INSERTED.Id INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Edited status
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    FROM @Changes as c
      INNER LOOP JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'commitstatus' AND ItemId = c.Id)
    OPTION (FORCE ORDER)

    -- New status
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'commitstatus', c.Id, 0
    FROM @Changes as c
    WHERE NOT EXISTS (
        SELECT * FROM SyncLog
        WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'commitstatus' AND ItemId = c.Id)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
