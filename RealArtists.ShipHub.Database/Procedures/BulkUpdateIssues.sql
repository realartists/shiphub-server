CREATE PROCEDURE [dbo].[BulkUpdateIssues]
  @RepositoryId BIGINT,
  @Issues IssueTableType READONLY,
  @Labels MappingTableType READONLY,
  @Assignees MappingTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [IssueId] BIGINT NOT NULL
  )

  MERGE INTO Issues WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT [Id], [UserId], [Number], [State], [Title], [Body], [MilestoneId], [Locked], [CreatedAt], [UpdatedAt], [ClosedAt], [ClosedById], [PullRequest], [Reactions]
    FROM @Issues
  ) as [Source]
  ON ([Target].[Id] = [Source].[Id])
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [UserId], [RepositoryId], [Number], [State], [Title], [Body], [MilestoneId], [Locked], [CreatedAt], [UpdatedAt], [ClosedAt], [ClosedById], [PullRequest], [Reactions])
    VALUES ([Id], [UserId], @RepositoryId, [Number], [State], [Title], [Body], [MilestoneId], [Locked], [CreatedAt], [UpdatedAt], [ClosedAt], [ClosedById], [PullRequest], [Reactions])
  -- Update (this bumps for label only changes too)
  WHEN MATCHED AND 
    ([Target].[UpdatedAt] < [Source].[UpdatedAt] 
      OR ([Target].[UpdatedAt] = [Source].[UpdatedAt] 
        AND (
          ([Source].[Reactions] IS NOT NULL AND ISNULL([Target].[Reactions], '') <> ISNULL([Source].[Reactions], ''))
          OR 
          ([Source].[ClosedById] IS NOT NULL AND ISNULL([Target].[ClosedById], 0) <> ISNULL([Source].[ClosedById], 0))
        )
      )
    ) THEN
    UPDATE SET
      [UserId] = [Source].[UserId], -- This can change to ghost
      [State] = [Source].[State],
      [Title] = [Source].[Title],
      [Body] = [Source].[Body],
      [MilestoneId] = [Source].[MilestoneId],
      [Locked] = [Source].[Locked],
      [UpdatedAt] = [Source].[UpdatedAt],
      [ClosedAt] = [Source].[ClosedAt],
      [ClosedById] = COALESCE([Source].[ClosedById], [Target].[ClosedById]),
      [PullRequest] = [Source].[PullRequest],
      [Reactions] = COALESCE([Source].[Reactions], [Target].[Reactions])
  OUTPUT INSERTED.Id INTO @Changes;

  MERGE INTO IssueLabels WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT Item1 AS IssueId, Item2 AS LabelId FROM @Labels
  ) as [Source]
  ON ([Target].LabelId = [Source].LabelId AND [Target].IssueId = [Source].IssueId)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (IssueId, LabelId)
    VALUES (IssueId, LabelId)
  -- Delete
  WHEN NOT MATCHED BY SOURCE
    AND [Target].IssueId IN (SELECT Id FROM @Issues)
    THEN DELETE
  OUTPUT COALESCE(INSERTED.IssueId, DELETED.IssueId) INTO @Changes;

  -- Assignees
  MERGE INTO IssueAssignees WITH(UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT Item1 as IssueId, Item2 as UserId FROM @Assignees
  ) as [Source]
  ON ([Target].IssueId = [Source].IssueId AND [Target].UserId = [Source].UserId)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (IssueId, UserId)
    VALUES (IssueId, UserId)
  -- Delete
  WHEN NOT MATCHED BY SOURCE
    AND [Target].IssueId IN (SELECT Id FROM @Issues)
    THEN DELETE
  OUTPUT COALESCE(INSERTED.IssueId, DELETED.IssueId) INTO @Changes;

  -- Update existing issues
  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [RowVersion] = DEFAULT
  WHERE OwnerType = 'repo'
    AND OwnerId = @RepositoryId
    AND ItemType = 'issue'
    AND ItemId IN (SELECT DISTINCT IssueId FROM @Changes)

  -- New issues
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'issue', c.IssueId, 0
  FROM (SELECT DISTINCT IssueId FROM @Changes) as c
  WHERE NOT EXISTS (
    SELECT * FROM SyncLog WITH (UPDLOCK)
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'issue' AND ItemId = c.IssueId)

  -- New Accounts
  INSERT INTO SyncLog WITH (SERIALIZABLE) (OwnerType, OwnerId, ItemType, ItemId, [Delete])
  SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
  FROM (
    SELECT DISTINCT(UPUserId) as UserId
    FROM Issues as c
        INNER JOIN @Changes as ch ON (c.Id = ch.IssueId)
      UNPIVOT (UPUserId FOR [Role] IN (UserId, ClosedById)) as [Ignored]
    UNION
    SELECT Item2 FROM @Assignees
  ) as c
  WHERE NOT EXISTS (
    SELECT * FROM SyncLog WITH (UPDLOCK)
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'account' AND ItemId = c.UserId)

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
