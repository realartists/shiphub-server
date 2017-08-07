CREATE PROCEDURE [dbo].[SyncSpiderProgress]
	@UserId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- Has fully synced repos list?
  SELECT CONVERT(BIT, (CASE WHEN RepoMetadataJson IS NULL THEN 0 ELSE 1 END)) AS HasRepoMetadata FROM Accounts WHERE Id = @UserId

  -- How many issues have we fetched so far per repo?
  SELECT R.Id AS RepositoryId, 
         R.IssuesFullyImported As IssuesFullyImported,
         CONVERT(BIT, (CASE WHEN R.IssueMetadataJson IS NULL THEN 0 ELSE 1 END)) AS HasIssueMetadata,
         (SELECT MAX(Number) FROM Issues WHERE RepositoryId = R.Id) AS MaxNumber,
         (SELECT COUNT(1) FROM Issues WHERE RepositoryId = R.Id) AS IssueCount
  FROM AccountRepositories AR
    INNER LOOP JOIN Repositories R ON (R.Id = AR.RepositoryId)
  WHERE AR.AccountId = @UserId
    AND R.[Disabled] = 0
  OPTION (FORCE ORDER)
END
