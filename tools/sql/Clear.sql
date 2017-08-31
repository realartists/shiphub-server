SET XACT_ABORT OFF
GO

DECLARE @Keep AS TABLE (
	[KeepId] BIGINT NOT NULL PRIMARY KEY
)

INSERT INTO @Keep (KeepId)
SELECT DISTINCT(UserId) FROM GitHubTokens

DELETE FROM [dbo].[AccountRepositories]
DELETE FROM [dbo].[Accounts] WHERE NOT EXISTS (SELECT * FROM @Keep WHERE KeepId = Id)
DELETE FROM [dbo].[AccountSyncRepositories]
DELETE FROM [dbo].[AccountSettings] WHERE NOT EXISTS (SELECT * FROM @Keep WHERE KeepId = AccountId)
DELETE FROM [dbo].[Comments]
DELETE FROM [dbo].[CommitComments]
DELETE FROM [dbo].[CommitStatuses]
DELETE FROM [dbo].[GitHubTokens] WHERE NOT EXISTS (SELECT * FROM @Keep WHERE KeepId = UserId)
DELETE FROM [dbo].[Hooks]
DELETE FROM [dbo].[IssueAssignees]
DELETE FROM [dbo].[IssueEventAccess]
DELETE FROM [dbo].[IssueEvents]
DELETE FROM [dbo].[IssueLabels]
DELETE FROM [dbo].[IssueMentions]
DELETE FROM [dbo].[Issues]
DELETE FROM [dbo].[Labels]
DELETE FROM [dbo].[Milestones]
DELETE FROM [dbo].[OrganizationAccounts]
DELETE FROM [dbo].[Projects]
DELETE FROM [dbo].[ProtectedBranches]
DELETE FROM [dbo].[PullRequestComments]
DELETE FROM [dbo].[PullRequestReviewers]
DELETE FROM [dbo].[PullRequests]
DELETE FROM [dbo].[Reactions]
DELETE FROM [dbo].[Repositories]
DELETE FROM [dbo].[RepositoryAccounts]
DELETE FROM [dbo].[Reviews]
DELETE FROM [dbo].[Subscriptions]
DELETE FROM [dbo].[SyncLog]
DELETE FROM [dbo].[Usage]

-- Metadata cleanup
UPDATE Accounts SET
	MetadataJson = NULL,
	RepoMetadataJson = NULL,
	OrgMetadataJson = NULL,
	ProjectMetadataJson = NULL,
	MentionMetadataJson = NULL,
	MentionSince = NULL

GO

--DBCC FREEPROCCACHE
--GO

--EXEC sp_updatestats
--GO