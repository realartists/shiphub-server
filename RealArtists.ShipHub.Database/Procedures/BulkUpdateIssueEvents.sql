CREATE PROCEDURE [dbo].[BulkUpdateIssueEvents]
  @RepositoryId BIGINT,
  @IssueEvents IssueEventTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO IssueEvents as [Target]
  USING (
    SELECT [Id], @RepositoryId as [RepositoryId], [CreatedAt], [ExtensionData]
    FROM @IssueEvents
  ) as [Source]
  ON ([Target].[Id] = [Source].[Id])
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [RepositoryId], [CreatedAt], [ExtensionData])
    VALUES ([Id], [RepositoryId], [CreatedAt], [ExtensionData]);
END
