CREATE PROCEDURE [dbo].[DeleteRepositories]
  @Repositories ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    -- Projects
    DELETE FROM Projects
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Hooks
    DELETE FROM Hooks
    WHERE EXISTS (SELECT * FROM @Repositories
    WHERE RepositoryId IS NOT NULL AND Item = RepositoryId)

    -- RepositoryAccounts
    DELETE FROM RepositoryAccounts
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- SyncLog
    DELETE FROM SyncLog
    WHERE OwnerType = 'repo'
      AND EXISTS (SELECT * FROM @Repositories WHERE Item = OwnerId)

    -- AccountRepositories
    DELETE FROM AccountRepositories
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- IssueEventAccess
    DELETE FROM IssueEventAccess
    FROM IssueEventAccess as iea
      INNER JOIN IssueEvents as ie ON (ie.Id = iea.IssueEventId)
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = ie.RepositoryId)

    -- IssueEvents
    DELETE FROM IssueEvents
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)
  
    -- IssueAssignees
    DELETE FROM IssueAssignees
    FROM IssueAssignees as ia
      INNER JOIN Issues as i ON (i.Id = ia.IssueId)
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = i.RepositoryId)

    -- Pull Request Reviewers
    DELETE FROM PullRequestReviewers
    FROM PullRequestReviewers as prr
      INNER JOIN Issues as i ON (i.Id = prr.IssueId)
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = i.RepositoryId)

    -- IssueLabels
    DELETE FROM IssueLabels
    FROM IssueLabels as il
      INNER JOIN Issues as i ON (i.Id = il.IssueId)
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = i.RepositoryId)

    -- Reactions
    DELETE FROM Reactions
    FROM Reactions as r
      INNER JOIN Issues as i ON (i.Id = r.IssueId)
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = i.RepositoryId)

    DELETE FROM Reactions
    FROM Reactions as r
      INNER JOIN Comments as c ON (c.Id = r.CommentId)
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = c.RepositoryId)

    DELETE FROM Reactions
    FROM Reactions as r
      INNER JOIN CommitComments as cc ON (cc.Id = r.CommitCommentId)
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = cc.RepositoryId)

    DELETE FROM Reactions
    FROM Reactions as r
      INNER JOIN PullRequestComments as prc ON (prc.Id = r.PullRequestCommentId)
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = prc.RepositoryId)

    -- Commit Statuses
    DELETE FROM CommitStatuses
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Comments
    DELETE FROM Comments
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Commit Comments
    DELETE FROM CommitComments
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Pull Request Comments
    DELETE FROM PullRequestComments
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Pull Request Reviews
    DELETE FROM Reviews
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Pull Requests
    DELETE FROM PullRequests
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Issues
    DELETE FROM Issues
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Milestones
    DELETE FROM Milestones
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Labels
    DELETE FROM Labels
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = RepositoryId)

    -- Repositories
    DELETE FROM Repositories
    WHERE EXISTS (SELECT * FROM @Repositories WHERE Item = Id)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
