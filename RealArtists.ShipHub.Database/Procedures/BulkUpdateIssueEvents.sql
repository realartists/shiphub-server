CREATE PROCEDURE [dbo].[BulkUpdateIssueEvents]
  @RepositoryId BIGINT,
  @IssueEvents IssueEventTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @Changes TABLE (
    [IssueEventId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  );

  MERGE INTO IssueEvents WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT Id, IssueId, ActorId, CommitId, [Event], CreatedAt, AssigneeId, ExtensionData
    FROM @IssueEvents
  ) as [Source]
  ON ([Target].[Id] = [Source].[Id])
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, RepositoryId, IssueId, ActorId, CommitId, [Event], CreatedAt, AssigneeId, ExtensionData)
    VALUES (Id, @RepositoryId, IssueId, ActorId, CommitId, [Event], CreatedAt, AssigneeId, ExtensionData)
  OUTPUT INSERTED.Id INTO @Changes (IssueEventId)
  OPTION (RECOMPILE);

  -- Events are only ever added
  INSERT INTO RepositoryLog WITH (SERIALIZABLE) (RepositoryId, [Type], ItemId, [Delete])
  SELECT @RepositoryId, 'event', IssueEventId, 0
  FROM @Changes

  -- Milestones referenced are either already referenced or have been deleted.

  -- Add missing account references to log
  ;MERGE INTO RepositoryLog WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT Distinct(UPUserId) as UserId
    FROM IssueEvents as e
        INNER JOIN @Changes as c ON (e.Id = c.IssueEventId)
      UNPIVOT (UPUserId FOR [Role] IN (ActorId, AssigneeId)) [Ignore]
  ) as [Source]
  ON ([Target].RepositoryId = @RepositoryId
    AND [Target].[Type] = 'account'
    AND [Target].ItemId = [Source].UserId)
  -- Insert
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, [Type], ItemId, [Delete])
    VALUES (@RepositoryId, 'account', [Source].UserId, 0)
  OPTION (RECOMPILE);
END
