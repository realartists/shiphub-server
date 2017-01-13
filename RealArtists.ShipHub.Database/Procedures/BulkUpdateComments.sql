CREATE PROCEDURE [dbo].[BulkUpdateComments]
  @RepositoryId BIGINT,
  @Comments CommentTableType READONLY
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

    MERGE INTO Comments WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT c.Id, COALESCE(c.IssueId, i.Id) as IssueId, c.UserId, c.Body, c.CreatedAt, c.UpdatedAt
      FROM @Comments as c
        LEFT OUTER JOIN Issues as i ON (i.RepositoryId = @RepositoryId AND i.Number = c.IssueNumber AND c.IssueId IS NULL)
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, IssueId, RepositoryId, UserId, Body, CreatedAt, UpdatedAt)
      VALUES (Id, IssueId, @RepositoryId, UserId, Body, CreatedAt, UpdatedAt)
    -- Update
    WHEN MATCHED AND [Target].[UpdatedAt] < [Source].[UpdatedAt] THEN
      UPDATE SET
        [UserId] = [Source].[UserId], -- You'd think this couldn't change, but it can become the Ghost
        [Body] = [Source].[Body],
        [UpdatedAt] = [Source].[UpdatedAt]
    OUTPUT INSERTED.Id, INSERTED.UserId INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Edited comments
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    WHERE ItemType = 'comment'
      AND ItemId IN (SELECT Id FROM @Changes)

    -- New comments
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'comment', c.Id, 0
    FROM @Changes as c
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog
      WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'comment' AND ItemId = c.Id)

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
