CREATE PROCEDURE [dbo].[BulkUpdateLabels]
  @RepositoryId BIGINT,
  @Labels LabelTableType READONLY,
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

  -- We don't need to track the issues modified because the client
  -- is smart enough to remove deleted labels from issues.
  DELETE FROM IssueLabels
  FROM IssueLabels as il WITH (UPDLOCK SERIALIZABLE)
    INNER JOIN Issues as i ON (i.Id = il.IssueId)
  WHERE i.RepositoryId = @RepositoryId
    AND il.LabelId NOT IN (SELECT Id FROM @Labels)
    AND @Complete = 1

  MERGE INTO Labels WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT Id, Name, Color
    FROM @Labels
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, RepositoryId, Name, Color)
    VALUES (Id, @RepositoryId, Name, Color)
  -- Delete
  WHEN NOT MATCHED BY SOURCE AND (@Complete = 1 AND [Target].RepositoryId = @RepositoryId) THEN DELETE
  -- Update
  WHEN MATCHED AND ([Source].Name != [Target].Name OR [Source].Color != [Target].Color) THEN
    UPDATE SET
      Name = [Source].Name,
      Color = [Source].Color
  OUTPUT ISNULL(INSERTED.Id, DELETED.Id), $action INTO @Changes (Id, [Action]);

  -- Deleted or edited labels
  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = CAST(CASE WHEN [Action] = 'DELETE' THEN 1 ELSE 0 END as BIT),
    [RowVersion] = DEFAULT
  FROM RepositoryLog as rl
    INNER JOIN @Changes as c ON (c.Id = rl.ItemId)
  WHERE RepositoryId = @RepositoryId AND [Type] = 'label'

  -- New milestones
  INSERT INTO RepositoryLog WITH (SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'label', c.Id, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT * FROM RepositoryLog WITH (UPDLOCK) WHERE ItemId = c.Id AND RepositoryId = @RepositoryId AND [Type] = 'label')

  -- Return repository if updated
  SELECT NULL as OrganizationId, @RepositoryId as RepositoryId, NULL as UserId
  WHERE EXISTS (SELECT * FROM @Changes)
END
