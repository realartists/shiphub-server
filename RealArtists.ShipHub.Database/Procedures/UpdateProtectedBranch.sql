CREATE PROCEDURE [dbo].[UpdateProtectedBranch]
  @RepositoryId BIGINT,
  @Name NVARCHAR(MAX),
  @Protection NVARCHAR(MAX),
  @ProtectionMetadataJson NVARCHAR(MAX)
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes TABLE (
    [Id] BIGINT NOT NULL PRIMARY KEY
  );

  MERGE INTO ProtectedBranches WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT @RepositoryId AS RepositoryId, @Name as [Name], @Protection AS Protection
  ) AS [Source]
  ON ([Target].RepositoryId = [Source].RepositoryId AND [Target].[Name] = [Source].[Name])
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Name], Protection, ProtectionMetadataJson)
    VALUES (@RepositoryId, @Name, @Protection, @ProtectionMetadataJson)
  -- Update 
  WHEN MATCHED AND [Target].[Protection] != [Source].[Protection] THEN
    UPDATE SET
      [Protection] = @Protection,
      [ProtectionMetadataJson] = @ProtectionMetadataJson
  OUTPUT Inserted.Id INTO @Changes
  OPTION (LOOP JOIN, FORCE ORDER);

  -- Update sync log
  UPDATE SyncLog SET
    [RowVersion] = DEFAULT
  WHERE ItemType = 'protectedbranch'
    AND ItemId IN (SELECT Id FROM @Changes)

  -- New branches
  INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'protectedbranch', c.Id, 0
    FROM (SELECT Id FROM @Changes) as c
   WHERE NOT EXISTS (
    SELECT * FROM SyncLog
     WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'protectedbranch' AND ItemId = c.Id);

  -- Output sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes);
END
