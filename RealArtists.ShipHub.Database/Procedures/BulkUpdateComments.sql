CREATE PROCEDURE [dbo].[BulkUpdateComments]
  @RepositoryId BIGINT,
  @Comments CommentTableType READONLY,
  @Complete BIT = 0
WITH RECOMPILE
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @Changes TABLE (
    [Id]     BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [Action] NVARCHAR(10) NOT NULL
  );

  MERGE INTO Comments WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT c.Id, i.Id as [IssueId], c.UserId, c.Body, c.CreatedAt, c.UpdatedAt, c.Reactions
    FROM @Comments as c
      INNER JOIN [Issues] as i ON (i.RepositoryId = @RepositoryId AND i.Number = c.[IssueNumber])
  ) as [Source]
  ON ([Target].[Id] = [Source].[Id])
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [IssueId], [RepositoryId], [UserId], [Body], [CreatedAt], [UpdatedAt], [Reactions])
    VALUES ([Id], [IssueId], @RepositoryId, [UserId], [Body], [CreatedAt], [UpdatedAt], [Reactions])
  -- Delete
  WHEN NOT MATCHED BY SOURCE AND (@Complete = 1 AND [Target].RepositoryId = @RepositoryId) THEN DELETE
  -- Update
  WHEN MATCHED AND [Target].[UpdatedAt] < [Source].[UpdatedAt] THEN
    UPDATE SET
      [UserId] = [Source].[UserId], -- You'd think this couldn't change, but it can become the Ghost
      [Body] = [Source].[Body],
      [UpdatedAt] = [Source].[UpdatedAt],
      [Reactions] = [Source].[Reactions]
  OUTPUT COALESCE(INSERTED.Id, DELETED.Id), $action INTO @Changes (Id, [Action]);

  -- Add comment changes to log
  MERGE INTO RepositoryLog WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT Id, CAST(CASE WHEN [Action] = 'DELETE' THEN 1 ELSE 0 END as BIT) as [Delete]
    FROM @Changes
  ) as [Source]
  ON ([Target].RepositoryId = @RepositoryId
    AND [Target].[Type] = 'comment'
    AND [Target].ItemId = [Source].Id)
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (@RepositoryId, 'comment', Id, [Delete])
  -- Update/Delete
  WHEN MATCHED THEN
    UPDATE SET
      [Delete] = [Source].[Delete],
      [RowVersion] = NULL; -- Causes new ID to be assigned by trigger

  -- Add new account references to log
  MERGE INTO RepositoryLog WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT DISTINCT(UserId)
    FROM Comments as c
      INNER JOIN @Changes as ch ON (c.Id = ch.Id AND ch.[Action] = 'INSERT')
  ) as [Source]
  ON ([Target].RepositoryId = @RepositoryId
    AND [Target].[Type] = 'account'
    AND [Target].ItemId = [Source].UserId)
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (@RepositoryId, 'account', [Source].UserId, 0);
END
