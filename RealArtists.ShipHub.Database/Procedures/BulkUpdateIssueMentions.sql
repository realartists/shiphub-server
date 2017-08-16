CREATE PROCEDURE [dbo].[BulkUpdateIssueMentions]
  @UserId BIGINT,
  @Issues ItemListTableType READONLY,
  @Complete BIT = 0
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

    -- Mentions
    MERGE INTO IssueMentions WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Item as IssueId FROM @Issues
    ) as [Source]
    ON ([Target].IssueId = [Source].IssueId AND [Target].UserId = @UserId)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (IssueId, UserId)
      VALUES (IssueId, @UserId)
    OUTPUT INSERTED.IssueId INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    IF(@Complete = 1)
    BEGIN
      -- Delete any extraneous mappings.
      DELETE FROM IssueMentions
      OUTPUT DELETED.IssueId INTO @Changes
      FROM IssueMentions as im
        LEFT OUTER JOIN @Issues as i ON (i.Item = im.IssueId)
      WHERE im.UserId = @UserId AND i.Item IS NULL
    END

    -- Update log
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    FROM (SELECT DISTINCT IssueId FROM @Changes) as c
      INNER LOOP JOIN SyncLog as sl ON (sl.OwnerType = 'repo' AND sl.ItemType = 'issue' AND sl.ItemId = c.IssueId)
    OPTION (FORCE ORDER)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return sync notifications
  -- In this case, the repo did change, but only the impacted user cares.
  SELECT 'user' as ItemType, @UserId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
