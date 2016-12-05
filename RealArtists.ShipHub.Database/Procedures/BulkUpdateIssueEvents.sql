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

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [IssueEventId] BIGINT NOT NULL
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
  OUTPUT INSERTED.Id INTO @Changes;

   -- Add access grants
  INSERT INTO IssueEventAccess WITH (SERIALIZABLE) (IssueEventId, UserId)
  OUTPUT INSERTED.IssueEventId INTO @Changes
  SELECT Id, @UserId
  FROM @IssueEvents as ie
  WHERE ie.Restricted = 1
    AND NOT EXISTS(SELECT * FROM IssueEventAccess WITH (UPDLOCK) WHERE IssueEventId = ie.Id AND UserId = @UserId)

  -- Update existing events
  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [RowVersion] = DEFAULT
  WHERE OwnerType = 'repo'
    AND OwnerId = @RepositoryId
    AND ItemType = 'event'
    AND ItemId IN (SELECT DISTINCT IssueEventId FROM @Changes)

  -- New events
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'event', c.IssueEventId, 0
  FROM (SELECT DISTINCT IssueEventId FROM @Changes) as c
  WHERE NOT EXISTS (
    SELECT * FROM SyncLog WITH (UPDLOCK)
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'event' AND ItemId = c.IssueEventId)

  -- New Accounts
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
  FROM (SELECT DISTINCT Item as UserId FROM @ReferencedAccounts) as c
  WHERE NOT EXISTS (
    SELECT * FROM SyncLog WITH (UPDLOCK)
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'account' AND ItemId = c.UserId)

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
