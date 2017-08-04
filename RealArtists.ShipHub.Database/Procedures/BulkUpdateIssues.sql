CREATE PROCEDURE [dbo].[BulkUpdateIssues]
  @RepositoryId BIGINT,
  @Issues IssueTableType READONLY,
  @Labels IssueMappingTableType READONLY,
  @Assignees IssueMappingTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [IssueId] BIGINT NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO Issues WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Id, UserId, Number, [State], Title, Body, MilestoneId, Locked, CreatedAt, UpdatedAt, ClosedAt, ClosedById, PullRequest, Reactions
      FROM @Issues
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, UserId,  RepositoryId, Number, [State], Title, Body, MilestoneId, Locked, CreatedAt, UpdatedAt, ClosedAt, ClosedById, PullRequest, Reactions)
      VALUES (Id, UserId, @RepositoryId, Number, [State], Title, Body, MilestoneId, Locked, CreatedAt, UpdatedAt, ClosedAt, ClosedById, PullRequest, Reactions)
    -- Update (this bumps for label only changes too)
    WHEN MATCHED AND (
        [Target].UpdatedAt < [Source].UpdatedAt
        OR ([Target].UpdatedAt = [Source].UpdatedAt
          AND (
            ([Source].Reactions IS NOT NULL AND ISNULL([Target].Reactions, '') <> ISNULL([Source].Reactions, ''))
            OR 
            ([Source].ClosedById IS NOT NULL AND ISNULL([Target].ClosedById, 0) <> ISNULL([Source].ClosedById, 0))
          )
        )
      ) THEN
      UPDATE SET
        UserId = [Source].UserId, -- This can change to ghost
        [State] = [Source].[State],
        Title = [Source].Title,
        Body = [Source].Body,
        MilestoneId = [Source].MilestoneId,
        Locked = [Source].Locked,
        UpdatedAt = [Source].UpdatedAt,
        ClosedAt = [Source].ClosedAt,
        ClosedById = ISNULL([Source].ClosedById, [Target].ClosedById),
        PullRequest = [Source].PullRequest,
        Reactions = ISNULL([Source].Reactions, [Target].Reactions)
    OUTPUT INSERTED.Id INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- LOOP JOIN, FORCE ORDER prevents scans
    -- This is (non-obviously) important when acquiring locks during foreign key validation
    -- Can't do the delete concurrently, as it'll insist on using a scan
    MERGE INTO IssueLabels WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT IssueId, MappedId AS LabelId FROM @Labels
    ) as [Source]
    ON ([Target].IssueId = [Source].IssueId AND [Target].LabelId = [Source].LabelId)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (IssueId, LabelId)
      VALUES (IssueId, LabelId)
    OUTPUT INSERTED.IssueId INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Delete any extraneous mappings.
    -- First, find all rows in IssueLabels for all issues in @WorkLabels
    -- Then join back on @WorkLabels to find unmatched rows.
    DELETE FROM IssueLabels
    OUTPUT DELETED.IssueId INTO @Changes
    FROM @Issues as i
      INNER LOOP JOIN IssueLabels as il ON (il.IssueId = i.Id)
      LEFT OUTER JOIN @Labels as ll ON (ll.IssueId = il.IssueId AND ll.MappedId = il.LabelId)
    WHERE ll.IssueId IS NULL
    OPTION (FORCE ORDER)

    -- Assignees
    -- Have to use the same tricks as above for the same reasons.
    MERGE INTO IssueAssignees WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT IssueId, MappedId as UserId FROM @Assignees
    ) as [Source]
    ON ([Target].IssueId = [Source].IssueId AND [Target].UserId = [Source].UserId)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (IssueId, UserId)
      VALUES (IssueId, UserId)
    OUTPUT INSERTED.IssueId INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Delete any extraneous mappings.
    -- Same tricks, same justification
    DELETE FROM IssueAssignees
    OUTPUT DELETED.IssueId INTO @Changes
    FROM @Issues as i
      INNER LOOP JOIN IssueAssignees as ia ON (ia.IssueId = i.Id)
      LEFT OUTER JOIN @Assignees as aa ON (aa.IssueId = ia.IssueId AND aa.MappedId = ia.UserId)
    WHERE aa.IssueId IS NULL
    OPTION (FORCE ORDER)

    -- Update existing issues
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT,
      [Delete] = 0
    FROM (SELECT DISTINCT IssueId FROM @Changes) as c
      INNER LOOP JOIN SyncLog as sl ON (sl.OwnerType = 'repo' AND sl.OwnerId = @RepositoryId AND sl.ItemType = 'issue' AND sl.ItemId = c.IssueId)
    OPTION (FORCE ORDER)

    -- New issues
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'issue', c.IssueId, 0
    FROM (SELECT DISTINCT IssueId FROM @Changes) as c
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog
      WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'issue' AND ItemId = c.IssueId)

    -- New Accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
    FROM (
      SELECT DISTINCT(UPUserId) as UserId
      FROM Issues as c
          INNER JOIN @Changes as ch ON (c.Id = ch.IssueId)
        UNPIVOT (UPUserId FOR [Role] IN (UserId, ClosedById)) as [Ignored]
      UNION
      SELECT MappedId FROM @Assignees
    ) as c
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
