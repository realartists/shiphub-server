CREATE PROCEDURE [dbo].[BulkUpdateCommitComments]
  @RepositoryId BIGINT,
  @Comments CommitCommentTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [Id]     BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [UserId] BIGINT NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO CommitComments WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Id, UserId, CommitId, [Path], Line, Position, Body, CreatedAt, UpdatedAt
      FROM @Comments
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id,  RepositoryId, UserId, CommitId, [Path], Line, Position, Body, CreatedAt, UpdatedAt)
      VALUES (Id, @RepositoryId, UserId, CommitId, [Path], Line, Position, Body, CreatedAt, UpdatedAt)
    -- Update
    WHEN MATCHED AND [Target].[UpdatedAt] < [Source].[UpdatedAt] THEN
      UPDATE SET
        UserId = [Source].UserId, -- You'd think this couldn't change, but it can become the Ghost
        Body = [Source].Body,
        UpdatedAt = [Source].UpdatedAt
    OUTPUT INSERTED.Id, INSERTED.UserId INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Deleted or edited comments
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    FROM @Changes as c
      INNER LOOP JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'commitcomment' AND ItemId = c.Id)
    OPTION (FORCE ORDER)

    -- New comments
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'commitcomment', c.Id, 0
    FROM @Changes as c
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog
      WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'commitcomment' AND ItemId = c.Id)

    -- New Accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
    FROM (SELECT DISTINCT UserId FROM @Changes) as c
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
