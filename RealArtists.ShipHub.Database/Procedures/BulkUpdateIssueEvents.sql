CREATE PROCEDURE [dbo].[BulkUpdateIssueEvents]
  @RepositoryId BIGINT,
  @IssueEvents IssueEventTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @Changes TABLE (
    [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  );

  MERGE INTO IssueEvents as [Target]
  USING (
    SELECT [Id], @RepositoryId as [RepositoryId], [CreatedAt], [ExtensionData]
    FROM @IssueEvents
  ) as [Source]
  ON ([Target].[Id] = [Source].[Id])
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [RepositoryId], [CreatedAt], [ExtensionData])
    VALUES ([Id], [RepositoryId], [CreatedAt], [ExtensionData])
  OUTPUT INSERTED.Id INTO @Changes (Id);
  
  -- Events are only ever added
  INSERT INTO RepositoryLog (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'event', Id, 0
  FROM @Changes

  -- Other data is inlined, so we don't need to record it in the log.
  -- Not ideal, and hope to fix later. Think it only affects users.
END
