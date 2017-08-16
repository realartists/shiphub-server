CREATE PROCEDURE [dbo].[ForceResyncRepositoryIssues]
  @RepositoryId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @RemoveFromSyncLog TABLE (
    [ItemId] BIGINT NOT NULL,
    [ItemType] NVARCHAR(20) NOT NULL
  );

  DECLARE @MarkDeletedInSyncLog TABLE (
    [ItemId] BIGINT NOT NULL,
    [ItemType] NVARCHAR(20) NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    -- OVERVIEW:
    -- There are two types of entities to delete here
    -- 1. Issues themselves
    -- 2. All other entities that hang off of issues:
    --    Reactions
    --    Comments
    --    IssueEvents
    --    IssueLabels
    --    IssueAssignees
    --    PullRequestComments
    --    PullRequestReviews
    --    PullRequests
    --
    -- So we delete all of the entities in class 2 first, bottom up according to our foreign key constraints.
    -- These guys we can safely just remove from the SyncLog, as the cascade order on the client doesn't require us to send deletes for them.
    --
    -- And then, finally, we delete the issues themselves, and put deletes in the SyncLog for them.


    DELETE Reactions
    OUTPUT Deleted.Id, 'reaction' INTO @RemoveFromSyncLog
      FROM Reactions
      JOIN Issues ON (Issues.Id = Reactions.IssueId)
     WHERE Issues.RepositoryId = @RepositoryId;

    DELETE Reactions
    OUTPUT Deleted.Id, 'reaction' INTO @RemoveFromSyncLog
      FROM Reactions
      JOIN Comments ON (Comments.Id = Reactions.CommentId)
     WHERE Comments.RepositoryId = @RepositoryId;

    DELETE Reactions
    OUTPUT Deleted.Id, 'reaction' INTO @RemoveFromSyncLog
      FROM Reactions
      JOIN PullRequestComments ON (PullRequestComments.Id = Reactions.PullRequestCommentId)
     WHERE PullRequestComments.RepositoryId = @RepositoryId;

    DELETE Reactions
    OUTPUT Deleted.Id, 'reaction' INTO @RemoveFromSyncLog
      FROM Reactions
      JOIN CommitComments ON (CommitComments.Id = Reactions.CommitCommentId)
     WHERE CommitComments.RepositoryId = @RepositoryId;
    
    DELETE FROM Comments
    OUTPUT Deleted.Id, 'comment' INTO @RemoveFromSyncLog
     WHERE RepositoryId = @RepositoryId;

    DELETE FROM IssueEventAccess
    FROM IssueEventAccess as iea
    INNER JOIN IssueEvents as ie on (ie.Id = iea.IssueEventId)
    WHERE ie.RepositoryId = @RepositoryId;

    DELETE FROM IssueEvents
    OUTPUT Deleted.Id, 'event' INTO @RemoveFromSyncLog
     WHERE RepositoryId = @RepositoryId;

    DELETE IssueLabels
      FROM IssueLabels
      JOIN Issues ON (Issues.Id = IssueLabels.IssueId)
     WHERE Issues.RepositoryId = @RepositoryId

    DELETE IssueAssignees
      FROM IssueAssignees
      JOIN Issues ON (Issues.Id = IssueAssignees.IssueId)
     WHERE Issues.RepositoryId = @RepositoryId

    DELETE FROM PullRequestReviewers
    FROM PullRequestReviewers as prr
      INNER JOIN Issues as i ON (i.Id = prr.IssueId)
    WHERE i.RepositoryId = @RepositoryId;

    -- DO NOT DELETE IssueMentions!
    -- They're allowed to remain in case mentioned issues are later synced.

    DELETE FROM PullRequestComments
    OUTPUT Deleted.Id, 'prcomment' INTO @RemoveFromSyncLog
     WHERE RepositoryId = @RepositoryId;

    DELETE FROM Reviews
    OUTPUT Deleted.Id, 'review' INTO @RemoveFromSyncLog
     WHERE RepositoryId = @RepositoryId;

    DELETE FROM PullRequests
     WHERE RepositoryId = @RepositoryId;
     
    -- End cascade-able entities, now handle issues themselves

    DELETE FROM Issues
    OUTPUT Deleted.Id, 'issue' INTO @MarkDeletedInSyncLog
     WHERE RepositoryId = @RepositoryId;

    -- Update SyncLog

    DELETE SyncLog
      FROM SyncLog
      JOIN @RemoveFromSyncLog AS R ON (R.ItemId = SyncLog.ItemId AND R.ItemType = SyncLog.ItemType AND SyncLog.OwnerType = 'repo' AND SyncLog.OwnerId = @RepositoryId);
    
    UPDATE SyncLog
       SET [RowVersion] = DEFAULT, [Delete] = 1
      FROM SyncLog
      JOIN @MarkDeletedInSyncLog AS M ON (M.ItemId = SyncLog.ItemId AND M.ItemType = SyncLog.ItemType AND SyncLog.OwnerType = 'repo' AND SyncLog.OwnerId = @RepositoryId);

    -- Finally, reset our sync nibble progress

    UPDATE Repositories
       SET IssuesFullyImported = 0, 
           IssueSince = NULL, 
           IssueMetadataJson = NULL,
           CommentMetadataJson = NULL,
           CommentSince = NULL,
           PullRequestMetadataJson = NULL,
           PullRequestUpdatedAt = NULL,
           PullRequestSkip = NULL
     WHERE Id = @RepositoryId;

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return sync notifications
  SELECT 'repo' as ItemType, @RepositoryId as ItemId;
RETURN 0
END
