CREATE PROCEDURE [dbo].[ExcessHooks]
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  SELECT ar.AccountId, r.FullName as RepoFullName, h.Id, h.GitHubId
  FROM Hooks as h
    INNER JOIN AccountRepositories as ar ON (ar.RepositoryId = h.RepositoryId)
    INNER JOIN Repositories as r ON (r.Id = h.RepositoryId)
  WHERE ar.[Admin] = 1
    AND NOT EXISTS (SELECT * FROM AccountSyncRepositories as asr WHERE asr.RepositoryId = h.RepositoryId)
  ORDER BY h.Id ASC
END
