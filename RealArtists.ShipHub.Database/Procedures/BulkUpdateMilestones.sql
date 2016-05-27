CREATE PROCEDURE [dbo].[BulkUpdateMilestones]
  @RepositoryId BIGINT,
  @Milestones MilestoneTableType READONLY,
  @Complete BIT = 0
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @Changes TABLE (
    [Id]     BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [Action] NVARCHAR(10) NOT NULL
  );

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
  OUTPUT COALESCE(INSERTED.Id, DELETED.Id), $action INTO @Changes (Id, [Action]);

  -- Add milestone changes to log
  MERGE INTO RepositoryLog as [Target]
  USING (
    SELECT Id, CAST(CASE WHEN [Action] = 'DELETE' THEN 1 ELSE 0 END as BIT) as [Delete]
    FROM @Changes
  ) as [Source]
  ON ([Target].RepositoryId = @RepositoryId
    AND [Target].[Type] = 'milestone'
    AND [Target].ItemId = [Source].Id)
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (@RepositoryId, 'milestone', Id, [Delete])
  -- Update/Delete
  WHEN MATCHED THEN
    UPDATE SET
      [Delete] = [Source].[Delete],
      [RowVersion] = NULL; -- Causes new ID to be assigned by trigger
END
