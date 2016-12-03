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

  MERGE INTO Milestones WITH (UPDLOCK SERIALIZABLE) as [Target]
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
  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = IIF([Action] = 'DELETE', 1, 0),
    [RowVersion] = DEFAULT
  FROM @Changes as c
    INNER JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'milestone' AND ItemId = c.Id)

  -- New milestones
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'milestone', c.Id, 0
  FROM @Changes as c
  WHERE NOT EXISTS (
    SELECT * FROM SyncLog WITH (UPDLOCK)
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'milestone' AND ItemId = c.Id)

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
