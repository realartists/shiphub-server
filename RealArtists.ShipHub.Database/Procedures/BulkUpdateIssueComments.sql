CREATE PROCEDURE [dbo].[BulkUpdateIssueComments]
  @RepositoryFullName NVARCHAR(510),
  @IssueNumber INT,
  @Comments CommentTableType READONLY,
  @Complete BIT = 0
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to repo log
  DECLARE @Changes TABLE (
    [Id]     BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [UserId] BIGINT       NOT NULL,
    [Action] NVARCHAR(10) NOT NULL
  )

  DECLARE @RepositoryId BIGINT
  SELECT @RepositoryId = Id FROM Repositories WHERE FullName = @RepositoryFullName

  IF (@RepositoryId IS NULL) RETURN -1;

  DECLARE @IssueId BIGINT
  SELECT @IssueId = Id FROM Issues WHERE RepositoryId = @RepositoryId AND Number = @IssueNumber

  IF (@IssueId IS NULL) RETURN -1;

  MERGE INTO Comments WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT c.Id, c.UserId, c.Body, c.CreatedAt, c.UpdatedAt
    FROM @Comments as c
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, IssueId, RepositoryId, UserId, Body, CreatedAt, UpdatedAt)
    VALUES (Id, @IssueId, @RepositoryId, UserId, Body, CreatedAt, UpdatedAt)
  -- Delete
  WHEN NOT MATCHED BY SOURCE AND (
    @Complete = 1
    AND [Target].RepositoryId = @RepositoryId
    AND [Target].IssueId = @IssueId) THEN DELETE
  -- Update
  WHEN MATCHED AND [Target].[UpdatedAt] < [Source].[UpdatedAt] THEN
    UPDATE SET
      [UserId] = [Source].[UserId], -- You'd think this couldn't change, but it can become the Ghost
      [Body] = [Source].[Body],
      [UpdatedAt] = [Source].[UpdatedAt]
  OUTPUT COALESCE(INSERTED.Id, DELETED.Id), COALESCE(INSERTED.UserId, DELETED.UserId), $action INTO @Changes (Id, UserId, [Action]);

  -- Deleted or edited comments
  UPDATE RepositoryLog WITH (SERIALIZABLE) SET
    [Delete] = CAST(CASE WHEN [Action] = 'DELETE' THEN 1 ELSE 0 END as BIT),
    [RowVersion] = DEFAULT
  FROM @Changes as c
    INNER JOIN RepositoryLog ON (ItemId = c.Id AND RepositoryId = @RepositoryId AND [Type] = 'comment')

  -- New comments
  INSERT INTO RepositoryLog WITH (SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'comment', c.Id, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT * FROM RepositoryLog WHERE ItemId = c.Id AND RepositoryId = @RepositoryId AND [Type] = 'comment')

  -- Add new account references to log
  MERGE INTO RepositoryLog WITH (SERIALIZABLE) as [Target]
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
