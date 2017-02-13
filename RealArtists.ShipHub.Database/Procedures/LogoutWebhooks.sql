CREATE PROCEDURE [dbo].[LogoutWebhooks]
  @UserId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- Repos from which to remove hooks.
  ;WITH RepoRollup as (
    SELECT r.Id, COUNT(*) as NumAdmins
    FROM AccountRepositories as ar
      INNER JOIN Repositories as r ON (r.Id = ar.RepositoryId)
      INNER JOIN Accounts as a ON (a.Id = ar.AccountId)
    WHERE ar.[Admin] = 1
      AND EXISTS (SELECT * FROM GitHubTokens WHERE UserId = ar.AccountId)
    GROUP BY r.Id
  ) SELECT r.FullName, h.GitHubId as HookId
  FROM AccountRepositories as ar
    INNER JOIN RepoRollup as rr ON (rr.Id = ar.RepositoryId)
    INNER JOIN Hooks as h ON (h.RepositoryId = ar.RepositoryId)
    INNER JOIN Repositories as r ON (r.Id = rr.Id)
  WHERE ar.AccountId = @UserId
    AND ar.[Admin] = 1
    AND rr.NumAdmins = 1
    AND h.GitHubId IS NOT NULL

  -- Orgs from which to remove hooks
  ;WITH OrgRollup as (
    SELECT oa.OrganizationId as Id, COUNT(*) as NumAdmins
    FROM OrganizationAccounts as oa
      INNER JOIN Accounts as a ON (a.Id = oa.UserId)
    WHERE oa.[Admin] = 1
      AND EXISTS (SELECT * FROM GitHubTokens WHERE UserId = oa.UserId)
    GROUP BY oa.OrganizationId
  ) SELECT o.[Login], h.GitHubId as HookId
  FROM OrganizationAccounts as oa
    INNER JOIN OrgRollup as r ON (r.Id = oa.OrganizationId)
    INNER JOIN Hooks as h ON (h.OrganizationId = r.Id)
    INNER JOIN Accounts as o ON (o.Id = r.Id)
  WHERE oa.UserId = @UserId
    AND oa.[Admin] = 1
    AND r.NumAdmins = 1
    AND h.GitHubId IS NOT NULL
END
