CREATE PROCEDURE [dbo].[SetRepositoryIssueTemplate]
	@RepositoryId BIGINT,
	@IssueTemplate NVARCHAR(MAX) NULL
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- Update the Repository with the new IssueTemplate
  UPDATE Repositories WITH (UPDLOCK SERIALIZABLE) SET 
    IssueTemplate = @IssueTemplate
  WHERE Id = @RepositoryId AND NULLIF(IssueTemplate, @IssueTemplate) IS NOT NULL

  DECLARE @Changes INT = @@ROWCOUNT

  -- Update the log with any changes
  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [RowVersion] = DEFAULT
  OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
  WHERE OwnerType = 'repo' AND OwnerId = @RepositoryId AND ItemType = 'repository' AND ItemId = @RepositoryId
    AND @Changes > 0
END
