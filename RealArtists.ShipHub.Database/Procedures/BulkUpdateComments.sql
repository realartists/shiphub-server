CREATE PROCEDURE [dbo].[BulkUpdateComments]
  @RepositoryId BIGINT,
  @Comments CommentTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to repo log
  DECLARE @Changes TABLE (
    [Id]     BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [UserId] BIGINT NOT NULL
  )

  MERGE INTO Comments WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT c.Id, ISNULL(c.IssueId, i.Id) as IssueId, c.UserId, c.Body, c.CreatedAt, c.UpdatedAt
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
  OUTPUT INSERTED.Id, INSERTED.UserId INTO @Changes (Id, UserId);

  -- Edited comments
  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [RowVersion] = DEFAULT
  FROM @Changes as c
    INNER JOIN RepositoryLog ON (ItemId = c.Id AND RepositoryId = @RepositoryId AND [Type] = 'comment')

  -- New comments
  INSERT INTO RepositoryLog WITH (UPDLOCK SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'comment', c.Id, 0
  FROM @Changes as c
  WHERE NOT EXISTS (SELECT * FROM RepositoryLog WHERE ItemId = c.Id AND RepositoryId = @RepositoryId AND [Type] = 'comment')

  -- Add new account references to log
  MERGE INTO RepositoryLog WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (SELECT DISTINCT(UserId) FROM @Changes) as [Source]
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
