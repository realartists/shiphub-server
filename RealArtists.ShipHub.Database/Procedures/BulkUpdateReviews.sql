CREATE PROCEDURE [dbo].[BulkUpdateReviews]
  @RepositoryId BIGINT,
  @IssueId BIGINT,
  @Date DATETIMEOFFSET,
  @Reviews ReviewTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [ReviewId] BIGINT       NOT NULL PRIMARY KEY CLUSTERED,
    [Action]   NVARCHAR(10) NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    -- Delete any extraneous reviews
    DELETE FROM Reviews
    OUTPUT DELETED.Id, 'DELETE' INTO @Changes
    FROM Reviews as r
      LEFT OUTER JOIN @Reviews as rr ON (rr.Id = r.Id)
    WHERE r.IssueId = @IssueId
      AND rr.Id IS NULL
    OPTION (FORCE ORDER)

    MERGE INTO Reviews WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Id, UserId, Body, CommitId, [State], SubmittedAt, [Hash]
      FROM @Reviews
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, IssueId, RepositoryId, UserId, Body, CommitId, [State], SubmittedAt, [Date], [Hash])
      VALUES (Id, @IssueId, @RepositoryId, UserId, Body, CommitId, [State], SubmittedAt, @Date, [Hash])
    WHEN MATCHED AND (
      [Target].[Date] < @Date AND [Source].[Hash] != [Target].[Hash]
    ) THEN UPDATE SET
      Body = [Source].Body,
      CommitId = [Source].CommitId,
      [State] = [Source].[State],
      SubmittedAt = [Source].SubmittedAt,
      [Date] = @Date,
      [Hash] = [Source].[Hash]
    OUTPUT INSERTED.Id, $action INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Deleted or edited reviews
    UPDATE SyncLog SET
      [Delete] = IIF([Action] = 'DELETE', 1, 0),
      [RowVersion] = DEFAULT
    FROM @Changes as c
      INNER LOOP JOIN SyncLog ON (OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'review' AND ItemId = c.ReviewId)
    OPTION (FORCE ORDER)

    -- New reviews
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'review', c.ReviewId, 0
    FROM @Changes as c
    WHERE c.[Action] = 'INSERT'
      AND NOT EXISTS (
        SELECT * FROM SyncLog
        WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'review' AND ItemId = c.ReviewId)

    -- New Accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT 'repo', @RepositoryId, 'account', c.UserId, 0
    FROM (SELECT DISTINCT UserId FROM @Reviews) as c
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
