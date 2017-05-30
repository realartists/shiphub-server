CREATE PROCEDURE [dbo].[UpdateRepositoryIssuesImported]
	@RepositoryId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes TABLE (
    Id BIGINT
  );

  UPDATE Repositories
     SET IssuesFullyImported = 1
  OUTPUT INSERTED.Id INTO @Changes
   WHERE Id = @RepositoryId AND IssuesFullyImported = 0;

  -- Update sync log. We have to do this as nothing else may have changed besides
  -- our realization that we're finished, so we have to update the client.
  UPDATE SyncLog SET
    [RowVersion] = DEFAULT
  WHERE ItemType = 'repository'
    AND ItemId IN (SELECT Id FROM @Changes)

  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END
