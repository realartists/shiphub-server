CREATE PROCEDURE [dbo].[BulkUpdateLabels]
  @RepositoryId BIGINT,
  @Labels LabelTableType READONLY,
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
  OUTPUT COALESCE(INSERTED.Id, DELETED.Id), $action INTO @Changes;

  -- Deleted or edited labels
  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = IIF([Action] = 'DELETE', 1, 0),
    [RowVersion] = DEFAULT
  FROM @Changes as c
    INNER JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'label' AND ItemId = c.Id)

  -- New labels
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'label', c.Id, 0
  FROM @Changes as c
  WHERE NOT EXISTS (
    SELECT * FROM SyncLog WITH (UPDLOCK)
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'label' AND ItemId = c.Id)

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
