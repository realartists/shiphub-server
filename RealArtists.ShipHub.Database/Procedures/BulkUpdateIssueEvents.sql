﻿CREATE PROCEDURE [dbo].[BulkUpdateIssueEvents]
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

  DECLARE @SyntheticIDs TABLE (
    [UniqueKey] NVARCHAR(255) NOT NULL PRIMARY KEY CLUSTERED,
    [Id] BIGINT NOT NULL DEFAULT (NEXT VALUE FOR [dbo].[SyntheticIssueEventIdentifier])
  );

  BEGIN TRY
    BEGIN TRANSACTION

    -- Allocate synthetic ids for every event that isn't already in IssueEvents and that didn't get one from GitHub
    INSERT INTO @SyntheticIDs (UniqueKey)
    SELECT src.UniqueKey FROM @IssueEvents AS src
    LEFT OUTER JOIN IssueEvents AS dst ON (src.UniqueKey = dst.UniqueKey)
    WHERE src.Id IS NULL AND dst.Id IS NULL;

    MERGE INTO IssueEvents as [Target]
    USING (
      SELECT Id, UniqueKey, IssueId, ActorId, [Event], CreatedAt, [Hash], Restricted, ExtensionData
      FROM @IssueEvents
    ) as [Source]
    ON ([Target].[UniqueKey] = [Source].[UniqueKey])
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, UniqueKey, RepositoryId, IssueId, ActorId, [Event], CreatedAt, [Hash], Restricted, Timeline, ExtensionData)
      VALUES (COALESCE(Id, (SELECT Id FROM @SyntheticIDs AS Syn WHERE Syn.UniqueKey = [Source].UniqueKey)), UniqueKey, @RepositoryId, IssueId, ActorId, [Event], CreatedAt, [Hash], Restricted, @Timeline, ExtensionData)
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
    INSERT INTO IssueEventAccess (IssueEventId, UserId)
    OUTPUT INSERTED.IssueEventId INTO @Changes
    SELECT ie.Id, @UserId
    FROM @IssueEvents AS x
    INNER JOIN IssueEvents AS ie ON (x.UniqueKey = ie.UniqueKey)
    WHERE ie.Restricted = 1
      AND NOT EXISTS(SELECT * FROM IssueEventAccess WHERE IssueEventId = ie.Id AND UserId = @UserId)

    -- Update existing events
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    WHERE OwnerType = 'repo'
      AND OwnerId = @RepositoryId
      AND ItemType = 'event'
      AND ItemId IN (SELECT DISTINCT IssueEventId FROM @Changes)

    -- New events
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'event', c.IssueEventId, 0
    FROM (SELECT DISTINCT IssueEventId FROM @Changes) as c
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog
      WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'event' AND ItemId = c.IssueEventId)

    -- New Accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
    FROM (SELECT DISTINCT Item as UserId FROM @ReferencedAccounts) as c
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog
      WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'account' AND ItemId = c.UserId)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
