CREATE PROCEDURE [dbo].[BulkUpdateIssueEvents]
  @UserId BIGINT,
  @RepositoryId BIGINT,
  @Timeline BIT,
  @IssueEvents IssueEventTableType READONLY,
  @ReferencedAccounts ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to repo log
  DECLARE @Changes TABLE (
    [IssueEventId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  DECLARE @AccessChanges TABLE (
    [IssueEventId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  MERGE INTO IssueEvents WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT Id, IssueId, ActorId, [Event], CreatedAt, [Hash], Restricted, ExtensionData
    FROM @IssueEvents
  ) as [Source]
  ON ([Target].[Id] = [Source].[Id])
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, RepositoryId, IssueId, ActorId, [Event], CreatedAt, [Hash], Restricted, Timeline, ExtensionData)
    VALUES (Id, @RepositoryId, IssueId, ActorId, [Event], CreatedAt, [Hash], Restricted, @Timeline, ExtensionData)
  WHEN MATCHED AND (
      [Source].[Hash] != [Target].[Hash] -- different
      AND ([Target].Timeline = 0 OR @Timeline = 1) -- prefer timeline over bulk issues
      AND ([Target].Restricted = 0 OR [Source].Restricted = 1) -- prefer more detailed events
  ) THEN
    UPDATE  SET
      ActorId = [Source].ActorId,
      [Event] = [Source].[Event],
      CreatedAt = [Source].CreatedAt,
      [Hash] = [Source].[Hash],
      Restricted = [Source].Restricted,
      Timeline = @Timeline,
      ExtensionData = [Source].ExtensionData
  OUTPUT INSERTED.Id INTO @Changes (IssueEventId);

   -- Add access grants
  INSERT INTO IssueEventAccess WITH (UPDLOCK SERIALIZABLE) (IssueEventId, UserId)
  OUTPUT INSERTED.IssueEventId INTO @AccessChanges (IssueEventId)
  SELECT Id, @UserId
  FROM @IssueEvents as ie
  WHERE ie.Restricted = 1
    AND NOT EXISTS(SELECT * FROM IssueEventAccess WHERE IssueEventId = ie.Id AND UserId = @UserId)

  INSERT INTO @Changes (IssueEventId)
  SELECT ac.IssueEventId
  FROM @AccessChanges as ac
  WHERE NOT EXISTS (SELECT * FROM @Changes WHERE IssueEventId = ac.IssueEventId)

  -- Update existing events
  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [RowVersion] = DEFAULT
  FROM RepositoryLog as rl
    INNER JOIN @Changes as c ON (rl.ItemId = c.IssueEventId)
  WHERE RepositoryId = @RepositoryId AND [Type] = 'event'

  -- New events
  INSERT INTO RepositoryLog WITH (UPDLOCK SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'event', c.IssueEventId, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT * FROM RepositoryLog WHERE ItemId = c.IssueEventId AND RepositoryId = @RepositoryId AND [Type] = 'event')

  -- Add missing account references to log
  MERGE INTO RepositoryLog WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT Item as UserId FROM @ReferencedAccounts
  ) as [Source]
  ON ([Target].RepositoryId = @RepositoryId
    AND [Target].[Type] = 'account'
    AND [Target].ItemId = [Source].UserId)
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (@RepositoryId, 'account', [Source].UserId, 0);

  -- Return repository if updated
  SELECT NULL as OrganizationId, @RepositoryId as RepositoryId, NULL as UserId
  WHERE EXISTS (SELECT * FROM @Changes)
END
