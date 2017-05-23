CREATE PROCEDURE [dbo].[DeleteReviewers]
  @RepositoryFullName NVARCHAR(510),
  @PullRequestNumber INT,
  @ReviewersJson NVARCHAR(MAX)
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @IssueId BIGINT
  DECLARE @RepositoryId BIGINT

  SELECT @IssueId = i.Id, @RepositoryId = i.RepositoryId
  FROM Issues as i
    INNER JOIN Repositories as r ON (r.Id = i.RepositoryId)
  WHERE i.Number = @PullRequestNumber
    AND r.FullName = @RepositoryFullName

  IF (@IssueId IS NULL) RETURN

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM PullRequestReviewers
    FROM PullRequestReviewers as prrs
      INNER LOOP JOIN Accounts as a ON (a.Id = prrs.UserId)
      INNER JOIN OPENJSON(@ReviewersJson) WITH ([Login] NVARCHAR(255) '$') as rs
        ON (rs.[Login] = a.[Login])
    WHERE prrs.IssueId = @IssueId
    OPTION (FORCE ORDER)

    -- Update issue
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'issue' AND ItemId = @IssueId

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId
END
