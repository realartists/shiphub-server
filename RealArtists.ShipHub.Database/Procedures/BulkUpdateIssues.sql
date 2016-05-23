CREATE PROCEDURE [dbo].[BulkUpdateIssues]
  @RepositoryId BIGINT,
  @Issues IssueTableType READONLY,
  @Labels LabelTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO Issues as [Target]
  USING (
    SELECT [Id], [UserId], @RepositoryId as [RepositoryId], [Number], [State], [Title], [Body], [AssigneeId], [MilestoneId], [Locked], [CreatedAt], [UpdatedAt], [ClosedAt], [ClosedById], [Reactions]
    FROM @Issues
  ) as [Source]
  ON ([Target].[Id] = [Source].[Id])
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [UserId], [RepositoryId], [Number], [State], [Title], [Body], [AssigneeId], [MilestoneId], [Locked], [CreatedAt], [UpdatedAt], [ClosedAt], [ClosedById], [Reactions])
    VALUES ([Id], [UserId], [RepositoryId], [Number], [State], [Title], [Body], [AssigneeId], [MilestoneId], [Locked], [CreatedAt], [UpdatedAt], [ClosedAt], [ClosedById], [Reactions])
  WHEN MATCHED AND [Target].[UpdatedAt] < [Source].[UpdatedAt] THEN
    UPDATE SET
      [UserId] = [Source].[UserId],
      [RepositoryId] = [Source].[RepositoryId],
      [Number] = [Source].[Number],
      [State] = [Source].[State],
      [Title] = [Source].[Title],
      [Body] = [Source].[Body],
      [AssigneeId] = [Source].[AssigneeId],
      [MilestoneId] = [Source].[MilestoneId],
      [Locked] = [Source].[Locked],
      [CreatedAt] = [Source].[CreatedAt],
      [UpdatedAt] = [Source].[UpdatedAt],
      [ClosedAt] = [Source].[ClosedAt],
      [ClosedById] = [Source].[ClosedById],
      [Reactions] = [Source].[Reactions];

  EXEC [dbo].[BulkCreateLabels] @Labels = @Labels

  MERGE INTO IssueLabels as [Target]
  USING (SELECT L1.Id as LabelId, L2.Id as IssueId
    FROM Labels as L1
      INNER JOIN @Labels as L2 ON (L1.Color = L2.Color AND L1.Name = L2.Name)
  ) as [Source]
  ON ([Target].LabelId = [Source].LabelId AND [Target].IssueId = [Source].IssueId)
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (IssueId, LabelId)
    VALUES (IssueId, LabelId)
  WHEN NOT MATCHED BY SOURCE
    AND [Target].IssueId IN (SELECT DISTINCT(Id) FROM @Labels)
    THEN DELETE;
END
