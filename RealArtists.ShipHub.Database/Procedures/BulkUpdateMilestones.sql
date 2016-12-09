CREATE PROCEDURE [dbo].[BulkUpdateMilestones]
  @RepositoryId BIGINT,
  @Milestones MilestoneTableType READONLY,
  @Complete BIT = 0
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [Id]     BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [Action] NVARCHAR(10) NOT NULL
  )

  DECLARE @Retries INT = 3
  
  WHILE(@Retries > 0)
  BEGIN
    BEGIN TRY
      BEGIN TRANSACTION

        MERGE INTO Milestones WITH (SERIALIZABLE) as [Target]
        USING (
          SELECT Id, Number, [State], Title, [Description], CreatedAt, UpdatedAt, ClosedAt, DueOn
          FROM @Milestones
        ) as [Source]
        ON ([Target].Id = [Source].Id)
        -- Add
        WHEN NOT MATCHED BY TARGET THEN
          INSERT (Id, RepositoryId, Number, [State], Title, [Description], CreatedAt, UpdatedAt, ClosedAt, DueOn)
          VALUES (Id, @RepositoryId, Number, [State], Title, [Description], CreatedAt, UpdatedAt, ClosedAt, DueOn)
        -- Delete
        WHEN NOT MATCHED BY SOURCE AND (@Complete = 1 AND [Target].RepositoryId = @RepositoryId) THEN DELETE
        -- Update
        WHEN MATCHED AND [Target].UpdatedAt < [Source].UpdatedAt THEN
          UPDATE SET
            Number = [Source].Number,
            [State] = [Source].[State], 
            Title = [Source].Title,
            [Description] = [Source].[Description],
            UpdatedAt = [Source].UpdatedAt,
            ClosedAt = [Source].ClosedAt,
            DueOn = [Source].DueOn
        OUTPUT COALESCE(INSERTED.Id, DELETED.Id), $action INTO @Changes;

        -- Deleted or edited milestones
        UPDATE SyncLog SET
          [Delete] = IIF([Action] = 'DELETE', 1, 0),
          [RowVersion] = DEFAULT
        FROM @Changes as c
          INNER JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'milestone' AND ItemId = c.Id)
        OPTION (FORCE ORDER)

        -- New milestones
        INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
        SELECT 'repo', @RepositoryId, 'milestone', c.Id, 0
        FROM @Changes as c
        WHERE NOT EXISTS (
          SELECT * FROM SyncLog
          WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'milestone' AND ItemId = c.Id)
        OPTION (FORCE ORDER)

      COMMIT TRANSACTION
      SET @Retries = 0
    END TRY
    BEGIN CATCH
      -- Unconditional actions run not matter what
      SET @Retries = @Retries - 1
      IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION
      DELETE @Changes -- Table variables don't participate in transactions

      -- Either retry or raise the error
      IF (
        ERROR_NUMBER() != 1205 -- Victim of deadlock
        OR @Retries = 0 -- We're giving up
      ) BEGIN
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE()
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY()
        DECLARE @ErrorState INT = ISNULL(NULLIF(ERROR_STATE(), 0), 1)

        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState)
      END
    END CATCH
  END -- WHILE

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
