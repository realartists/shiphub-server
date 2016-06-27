CREATE PROCEDURE [dbo].[BulkUpdateComments]
  @RepositoryId BIGINT,
  @Comments CommentTableType READONLY,
  @Complete BIT = 0
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @Changes TABLE (
    [Id]     BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [UserId] BIGINT       NOT NULL,
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
  OUTPUT COALESCE(INSERTED.Id, DELETED.Id), INSERTED.UserId, $action INTO @Changes (Id, UserId, [Action])
  OPTION (RECOMPILE);

  -- Deleted or edited comments
  UPDATE RepositoryLog WITH (SERIALIZABLE) SET
    [Delete] = CAST(CASE WHEN [Action] = 'DELETE' THEN 1 ELSE 0 END as BIT),
    [RowVersion] = DEFAULT
  FROM @Changes as c
    INNER JOIN RepositoryLog ON (ItemId = c.Id AND RepositoryId = @RepositoryId AND [Type] = 'comment')
  OPTION (RECOMPILE)

  -- New comments
  INSERT INTO RepositoryLog WITH (SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'comment', c.Id, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT 1 FROM RepositoryLog WHERE ItemId = c.Id AND RepositoryId = @RepositoryId AND [Type] = 'comment')
  OPTION (RECOMPILE)

  -- Add new account references to log
  MERGE INTO RepositoryLog WITH (SERIALIZABLE) as [Target]
  USING (SELECT DISTINCT(UserId) FROM @Changes WHERE [Action] = 'INSERT') as [Source]
  ON ([Target].ItemId = [Source].UserId
    AND [Target].RepositoryId = @RepositoryId
    AND [Target].[Type] = 'account')
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (@RepositoryId, 'account', [Source].UserId, 0)
  OPTION (RECOMPILE);
END
