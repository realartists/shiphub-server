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

  BEGIN TRY
    BEGIN TRANSACTION

    IF (@Complete = 1)
    BEGIN
      DELETE FROM Milestones
      OUTPUT DELETED.Id, 'DELETE' INTO @Changes
      FROM Milestones as m
        LEFT OUTER JOIN @Milestones as mm ON (mm.Id = m.Id)
      WHERE m.RepositoryId = @RepositoryId
        AND mm.Id IS NULL
      OPTION (FORCE ORDER)
    END

    -- LOOP JOIN, FORCE ORDER prevents scans
    -- This is (non-obviously) important when acquiring locks during foreign key validation
    MERGE INTO Milestones as [Target]
    USING (
      SELECT Id, Number, [State], Title, [Description], CreatedAt, UpdatedAt, ClosedAt, DueOn
      FROM @Milestones
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, RepositoryId, Number, [State], Title, [Description], CreatedAt, UpdatedAt, ClosedAt, DueOn)
      VALUES (Id, @RepositoryId, Number, [State], Title, [Description], CreatedAt, UpdatedAt, ClosedAt, DueOn)
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
    OUTPUT INSERTED.Id, $action INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Deleted or edited milestones
    UPDATE SyncLog SET
      [Delete] = IIF([Action] = 'DELETE', 1, 0),
      [RowVersion] = DEFAULT
    FROM @Changes as c
      INNER JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'milestone' AND ItemId = c.Id)

    -- New milestones
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'milestone', c.Id, 0
    FROM @Changes as c
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog
      WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'milestone' AND ItemId = c.Id)

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
