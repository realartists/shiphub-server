CREATE PROCEDURE [dbo].[BulkUpdateRepositories]
  @Date DATETIMEOFFSET,
  @Repositories RepositoryTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to repo log
  DECLARE @Changes TABLE (
    [Id]        BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [AccountId] BIGINT NOT NULL INDEX IX_Account NONCLUSTERED
  )

  -- Check for FullName collisions and assume the existing repo has been deleted
  DECLARE @DeletedRepositories ItemListTableType
  
  INSERT INTO @DeletedRepositories (Item)
  SELECT r.Id
  FROM @Repositories as rvar
    INNER JOIN Repositories as r ON (r.FullName = rvar.FullName AND r.Id != rvar.Id)
  
  IF EXISTS (SELECT * FROM @DeletedRepositories)
  BEGIN
    EXEC [dbo].[DeleteRepositories] @Repositories = @DeletedRepositories
  END

  -- It's business time
  MERGE INTO Repositories WITH (UPDLOCK SERIALIZABLE) as [Target]
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

  -- Bump existing repos
  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [RowVersion] = DEFAULT
  FROM RepositoryLog as rl
    INNER JOIN @Changes as c ON (rl.[Type] = 'repository' AND rl.ItemId = c.Id)

  -- New repositories reference themselves
  INSERT INTO RepositoryLog WITH (UPDLOCK SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT Id, 'repository', Id, 0
    FROM @Changes as c
  WHERE NOT EXISTS (
    SELECT *
    FROM RepositoryLog
    WHERE RepositoryId = c.Id AND [Type] = 'repository' AND ItemId = c.Id)

  -- Best to inline owners too
  INSERT INTO RepositoryLog WITH (UPDLOCK SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT Id, 'account', AccountId, 0
    FROM @Changes as c
  WHERE NOT EXISTS (
    SELECT *
    FROM RepositoryLog
    WHERE RepositoryId = c.Id AND [Type] = 'account' AND ItemId = c.AccountId)

  -- Return updated repositories
  SELECT NULL as OrganizationId, Id as RepositoryId, NULL as UserId
  FROM @Changes
END
