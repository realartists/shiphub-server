CREATE PROCEDURE [dbo].[BulkUpdateReactions]
  @RepositoryId BIGINT,
  @IssueId BIGINT = NULL,
  @CommentId BIGINT = NULL,
  @Reactions ReactionTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- Reactions are always submitted as the full list for the given item.
  -- This lets us track deletions

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [Id]     BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [UserId] BIGINT       NOT NULL,
    [Action] NVARCHAR(10) NOT NULL
  )

  MERGE INTO Reactions WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT Id, UserId, Content, CreatedAt
    FROM @Reactions
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, UserId, IssueId, CommentId, Content, CreatedAt)
    VALUES (Id, UserId, @IssueId, @CommentId, Content, CreatedAt)
  -- Delete
  WHEN NOT MATCHED BY SOURCE AND (IssueId = @IssueId OR CommentId = @CommentId)
    THEN DELETE
  OUTPUT COALESCE(INSERTED.Id, DELETED.Id), COALESCE(INSERTED.UserId, DELETED.UserId), $action INTO @Changes;

  -- Deleted or edited reactions
  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = IIF([Action] = 'DELETE', 1, 0),
    [RowVersion] = DEFAULT
  FROM @Changes as c
    INNER JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'reaction' AND ItemId = c.Id)

  -- New reactions
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'reaction', c.Id, 0
  FROM @Changes as c
  WHERE NOT EXISTS (
    SELECT * FROM SyncLog WITH (UPDLOCK)
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'reaction' AND ItemId = c.Id)

  -- New Accounts
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
  FROM (SELECT DISTINCT UserId FROM @Changes WHERE [Action] = 'INSERT') as c
  WHERE NOT EXISTS (
    SELECT * FROM SyncLog WITH (UPDLOCK)
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'account' AND ItemId = c.UserId)

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
