CREATE PROCEDURE [dbo].[DeleteRepositories]
  @Repositories ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- TODO: sp_getapplock sp_releaseapplock

  --Hooks
  DELETE FROM Hooks
  WHERE EXISTS (SELECT * FROM @Repositories
  WHERE RepositoryId IS NOT NULL AND Item = RepositoryId)

  --RepositoryAccounts
  DELETE FROM RepositoryAccounts
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

  --RepositoryLog
  DELETE FROM RepositoryLog
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

  --AccountRepositories
  DELETE FROM AccountRepositories
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

  --IssueEventAccess
  DELETE FROM IssueEventAccess
  FROM IssueEventAccess as iea
    INNER JOIN IssueEvents as ie ON (ie.Id = iea.IssueEventId)
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = ie.RepositoryId)

  --IssueEvents
  DELETE FROM IssueEvents
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)
  
  --IssueAssignees
  DELETE FROM IssueAssignees
  FROM IssueAssignees as ia
    INNER JOIN Issues as i ON (i.Id = ia.IssueId)
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = i.RepositoryId)

  --IssueLabels
  DELETE FROM IssueLabels
  FROM IssueLabels as il
    INNER JOIN Issues as i ON (i.Id = il.IssueId)
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = i.RepositoryId)

  --Reactions
  DELETE FROM Reactions
  FROM Reactions as r
    INNER JOIN Issues as i ON (i.Id = r.IssueId)
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = i.RepositoryId)

  DELETE FROM Reactions
  FROM Reactions as r
    INNER JOIN Comments as c ON (c.Id = r.CommentId)
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = c.RepositoryId)

  --Comments
  DELETE FROM Comments
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

  --Issues
  DELETE FROM Issues
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

  --Milestones
  DELETE FROM Milestones
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

  --Labels
  DELETE FROM Labels
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

  --Repositories
  DELETE FROM Repositories
  WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = Id)
END
