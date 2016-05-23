CREATE PROCEDURE [dbo].[BulkUpdateComments]
  @RepositoryId BIGINT,
  @Comments CommentTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO Comments as [Target]
  USING (
    SELECT c.[Id], i.[Id] as [IssueId], i.[RepositoryId], c.[UserId], c.[Body], c.[CreatedAt], c.[UpdatedAt], c.[Reactions]
    FROM @Comments as c
      INNER JOIN [Issues] as i ON (i.[RepositoryId] = @RepositoryId AND i.[Number] = c.[IssueNumber])
  ) as [Source]
  ON ([Target].[Id] = [Source].[Id])
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [IssueId], [RepositoryId], [UserId], [Body], [CreatedAt], [UpdatedAt], [Reactions])
    VALUES ([Id], [IssueId], [RepositoryId], [UserId], [Body], [CreatedAt], [UpdatedAt], [Reactions])
  WHEN MATCHED AND [Target].[UpdatedAt] < [Source].[UpdatedAt] THEN
    UPDATE SET
      [IssueId] = [Source].[IssueId],
      [RepositoryId] = [Source].[RepositoryId],
      [UserId] = [Source].[UserId],
      [Body] = [Source].[Body],
      [CreatedAt] = [Source].[CreatedAt],
      [UpdatedAt] = [Source].[UpdatedAt],
      [Reactions] = [Source].[Reactions];
END
