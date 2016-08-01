CREATE PROCEDURE [dbo].[BulkUpdateIssueEvents]
  @UserId BIGINT,
  @RepositoryId BIGINT,
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

  MERGE INTO IssueEvents WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT Id, IssueId, ActorId, [Event], CreatedAt, AssigneeId, [Hash], Restricted, ExtensionData
    FROM @IssueEvents
  ) as [Source]
  ON ([Target].[Id] = [Source].[Id])
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, RepositoryId, IssueId, ActorId, [Event], CreatedAt, AssigneeId, [Hash], Restricted, ExtensionData)
    VALUES (Id, @RepositoryId, IssueId, ActorId, [Event], CreatedAt, AssigneeId, [Hash], Restricted, ExtensionData)
  WHEN MATCHED AND [Source].[Hash] != [Target].[Hash] THEN
    UPDATE  SET
      ActorId = [Source].ActorId,
      [Event] = [Source].[Event],
      CreatedAt = [Source].CreatedAt,
      AssigneeId = [Source].AssigneeId,
      [Hash] = [Source].[Hash],
      Restricted = [Source].Restricted,
      ExtensionData = [Source].ExtensionData
  OUTPUT INSERTED.Id INTO @Changes (IssueEventId)
  OPTION (RECOMPILE);

   -- Add access grants
  INSERT INTO IssueEventAccess WITH (SERIALIZABLE) (IssueEventId, UserId)
  OUTPUT INSERTED.IssueEventId INTO @AccessChanges (IssueEventId)
  SELECT Id, @UserId
  FROM @IssueEvents as ie
  WHERE ie.Restricted = 1
    AND NOT EXISTS(SELECT * FROM IssueEventAccess WHERE IssueEventId = ie.Id AND UserId = @UserId)
  OPTION (RECOMPILE)

  INSERT INTO @Changes (IssueEventId)
  SELECT ac.IssueEventId
  FROM @AccessChanges as ac
  WHERE NOT EXISTS (SELECT * FROM @Changes WHERE IssueEventId = ac.IssueEventId)
  OPTION (RECOMPILE)

  -- Update existing events
  UPDATE RepositoryLog WITH (SERIALIZABLE) SET
    [RowVersion] = DEFAULT
  FROM RepositoryLog as rl
    INNER JOIN @Changes as c ON (rl.ItemId = c.IssueEventId)
  WHERE RepositoryId = @RepositoryId AND [Type] = 'event'
  OPTION (RECOMPILE)

  -- New events
  INSERT INTO RepositoryLog WITH (SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'event', c.IssueEventId, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT 1 FROM RepositoryLog WHERE ItemId = c.IssueEventId AND RepositoryId = @RepositoryId AND [Type] = 'event')
  OPTION (RECOMPILE)

  -- Add missing account references to log
  MERGE INTO RepositoryLog WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT Item as UserId FROM @ReferencedAccounts
  ) as [Source]
  ON ([Target].RepositoryId = @RepositoryId
    AND [Target].[Type] = 'account'
    AND [Target].ItemId = [Source].UserId)
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (@RepositoryId, 'account', [Source].UserId, 0)
  OPTION (RECOMPILE);

  -- Return repository if updated
  SELECT NULL as OrganizationId, @RepositoryId as RepositoryId, NULL as UserId
  WHERE EXISTS(SELECT 1 FROM @Changes)
  OPTION (RECOMPILE)
END
