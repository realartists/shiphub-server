CREATE PROCEDURE [dbo].[BulkUpdateRepositories]
  @Date DATETIMEOFFSET,
  @Repositories RepositoryTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO Repositories as [Target]
  USING (
    SELECT [Id], [AccountId], [Private], [Name], [FullName]
    FROM @Repositories
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [AccountId], [Private], [Name], [FullName], [Date])
    VALUES ([Id], [AccountId], [Private], [Name], [FullName], @Date)
  WHEN MATCHED
    AND [Target].[Date] < @Date 
    AND EXISTS (
      SELECT [Target].[AccountId], [Target].[Private], [Target].[Name], [Target].[FullName]
      EXCEPT
      SELECT [Source].[AccountId], [Source].[Private], [Source].[Name], [Source].[FullName]
    ) THEN
    UPDATE SET
      [AccountId] = [Source].[AccountId],
      [Private] = [Source].[Private],
      [Name] = [Source].[Name],
      [FullName] = [Source].[FullName],
      [Date] = @Date;
  --OUTPUT inserted.[Id], inserted.[AccountId], inserted.[Private], inserted.[Name], inserted.[FullName], inserted.[Date];
END
