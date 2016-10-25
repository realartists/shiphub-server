CREATE PROCEDURE [dbo].[BulkUpdateMilestones]
  @RepositoryId BIGINT,
  @Milestones MilestoneTableType READONLY,
  @Complete BIT = 0
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to repo log
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
  OUTPUT COALESCE(INSERTED.Id, DELETED.Id), $action INTO @Changes (Id, [Action]);

  -- Deleted or edited milestones
  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = CAST(CASE WHEN [Action] = 'DELETE' THEN 1 ELSE 0 END as BIT),
    [RowVersion] = DEFAULT
  FROM RepositoryLog as rl
    INNER JOIN @Changes as c ON (c.Id = rl.ItemId)
  WHERE RepositoryId = @RepositoryId AND [Type] = 'milestone'

  -- New milestones
  INSERT INTO RepositoryLog WITH (SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'milestone', c.Id, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT * FROM RepositoryLog WITH (UPDLOCK) WHERE ItemId = c.Id AND RepositoryId = @RepositoryId AND [Type] = 'milestone')

  -- Return repository if updated
  SELECT NULL as OrganizationId, @RepositoryId as RepositoryId, NULL as UserId
  WHERE EXISTS (SELECT * FROM @Changes)
END
