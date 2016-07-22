CREATE PROCEDURE [dbo].[BulkUpdateAccounts]
  @Date DATETIMEOFFSET,
  @Accounts AccountTableType READONLY,
  @MetaData MetaDataTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to repo log
  DECLARE @Changes TABLE (
    [Id]   BIGINT      NOT NULL PRIMARY KEY CLUSTERED,
    [Type] NVARCHAR(4) NOT NULL
  )

  -- For sync events
  DECLARE @Updates TABLE (
    [OrganizationId] BIGINT NULL,
    [RepositoryId]   BIGINT NULL
  )

  DECLARE @MetaDataMap TABLE (
    [ItemId]     BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [MetaDataId] BIGINT NOT NULL
  )

  MERGE INTO GitHubMetaData WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT ItemId, Id, ETag, Expires, LastModified, LastRefresh, AccountId
    FROM @MetaData
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (ETag, Expires, LastModified, LastRefresh, AccountId)
    VALUES (ETag, Expires, LastModified, LastRefresh, AccountId)
  -- Update
  WHEN MATCHED AND [Target].LastRefresh < [Source].LastRefresh
    THEN UPDATE SET
      ETag = [Source].ETag,
      Expires = [Source].Expires,
      LastModified = [Source].LastModified,
      LastRefresh = [Source].LastRefresh,
      AccountId = [Source].AccountId
  OUTPUT [Source].ItemId, INSERTED.Id as MetaDataId INTO @MetaDataMap (ItemId, MetaDataId)
  OPTION (RECOMPILE);

  MERGE INTO Accounts WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT Id, [Type], [Login]
    FROM @Accounts
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, [Type], [Login], [Date])
    VALUES (Id, [Type], [Login], @Date)
  -- Update
  WHEN MATCHED 
    AND [Target].[Date] < @Date
    AND EXISTS (
      SELECT [Target].[Type], [Target].[Login]
      EXCEPT
      SELECT [Source].[Type], [Source].[Login]
    ) THEN
    UPDATE SET
      [Type] = [Source].[Type],
      [Login] = [Source].[Login],
      [Date] = @Date
  OUTPUT INSERTED.Id, INSERTED.[Type] INTO @Changes (Id, [Type])
  OPTION (RECOMPILE);

  UPDATE Accounts WITH (SERIALIZABLE)
    SET MetaDataId = m.MetaDataId
  FROM Accounts as a
    INNER JOIN @MetaDataMap as m ON (a.Id = m.ItemId)
  WHERE a.MetaDataId IS NULL

  -- New Organizations reference themselves
  INSERT INTO OrganizationLog WITH (SERIALIZABLE) (OrganizationId, AccountId, [Delete])
  OUTPUT INSERTED.OrganizationId INTO @Updates (OrganizationId)
  SELECT c.Id, c.Id, 0
  FROM @Changes as c
  WHERE c.[Type] = 'org'
    AND NOT EXISTS (SELECT 1 FROM OrganizationLog WHERE OrganizationId = c.Id AND AccountId = c.Id)
  OPTION (RECOMPILE)

  -- Other actions manage adding user references to repos.
  -- Our only job here is to mark still valid references as changed.
  UPDATE RepositoryLog WITH (SERIALIZABLE) SET
    [RowVersion] = DEFAULT -- Bump version
  OUTPUT INSERTED.RepositoryId INTO @Updates (RepositoryId)
  WHERE [Type] = 'account'
    AND [Delete] = 0
    AND ItemId IN (SELECT Id FROM @Changes)
  OPTION (RECOMPILE)

  -- Return updated organizations and repositories
  SELECT DISTINCT OrganizationId, RepositoryId FROM @Updates OPTION (RECOMPILE)
END
