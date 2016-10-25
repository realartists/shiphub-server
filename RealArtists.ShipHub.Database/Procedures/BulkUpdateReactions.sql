﻿CREATE PROCEDURE [dbo].[BulkUpdateReactions]
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

  -- For tracking required updates to repo log
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
  OUTPUT COALESCE(INSERTED.Id, DELETED.Id), COALESCE(INSERTED.UserId, DELETED.UserId), $action INTO @Changes (Id, UserId, [Action]);

  -- Deleted or edited reactions
  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = CAST(CASE WHEN [Action] = 'DELETE' THEN 1 ELSE 0 END as BIT),
    [RowVersion] = DEFAULT
  FROM RepositoryLog as rl
    INNER JOIN @Changes as c ON (c.Id = rl.ItemId)
  WHERE RepositoryId = @RepositoryId AND [Type] = 'reaction'

  -- New reactions
  INSERT INTO RepositoryLog WITH (SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'reaction', c.Id, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT * FROM RepositoryLog WITH (UPDLOCK) WHERE ItemId = c.Id AND RepositoryId = @RepositoryId AND [Type] = 'reaction')

  -- Add new account references to log
  MERGE INTO RepositoryLog WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (SELECT DISTINCT(UserId) FROM @Changes WHERE [Action] = 'INSERT') as [Source]
  ON ([Target].ItemId = [Source].UserId
    AND [Target].RepositoryId = @RepositoryId
    AND [Target].[Type] = 'account')
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (@RepositoryId, 'account', [Source].UserId, 0);

  -- Return repository if updated
  SELECT NULL as OrganizationId, @RepositoryId as RepositoryId, NULL as UserId
  WHERE EXISTS (SELECT * FROM @Changes)
END
