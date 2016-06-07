CREATE PROCEDURE [dbo].[BulkUpdateRepositories]
  @Date DATETIMEOFFSET,
  @Repositories RepositoryTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @Changes TABLE (
    [Id]        BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [AccountId] BIGINT NOT NULL INDEX IX_Account NONCLUSTERED
  );

  MERGE INTO Repositories WITH (SERIALIZABLE) as [Target]
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
      [Date] = @Date
  OUTPUT INSERTED.Id, INSERTED.AccountId INTO @Changes (Id, AccountId);

  -- Add milestone changes to log
  MERGE INTO RepositoryLog WITH (SERIALIZABLE) as [Target]
  USING (SELECT Id FROM @Changes) as [Source]
  ON ([Target].RepositoryId = [Source].Id
    AND [Target].[Type] = 'repository'
    AND [Target].ItemId = [Source].Id)
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (Id, 'repository', Id, 0)
  -- Update
  WHEN MATCHED THEN UPDATE SET [RowVersion] = NULL; -- Causes new ID to be assigned by trigger

  -- Best to inline owners too
  MERGE INTO RepositoryLog WITH (SERIALIZABLE) as [Target]
  USING (SELECT Id, AccountId FROM @Changes) as [Source]
  ON ([Target].RepositoryId = [Source].Id
    AND [Target].[Type] = 'account'
    AND [Target].ItemId = [Source].AccountId)
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (Id, 'account', AccountId, 0);
END
