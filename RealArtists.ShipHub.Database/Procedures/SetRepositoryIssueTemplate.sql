CREATE PROCEDURE [dbo].[SetRepositoryIssueTemplate]
	@RepositoryId BIGINT,
	@IssueTemplate NVARCHAR(MAX) NULL
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    -- Update the Repository with the new IssueTemplate
    UPDATE Repositories SET 
      IssueTemplate = @IssueTemplate
    WHERE Id = @RepositoryId AND ISNULL(IssueTemplate, '') != ISNULL(@IssueTemplate, '')

    DECLARE @Changes INT = @@ROWCOUNT

    -- Update the log with any changes
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'repository' AND ItemId = @RepositoryId
      AND @Changes > 0

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
