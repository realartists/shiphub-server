CREATE TABLE [dbo].[PullRequestComments] (
  [Id]                  BIGINT         NOT NULL,
  [IssueId]             BIGINT         NOT NULL,
  [RepositoryId]        BIGINT         NOT NULL,
  [UserId]              BIGINT         NOT NULL,
  [PullRequestReviewId] BIGINT         NULL,
  [DiffHunk]            NVARCHAR(MAX)  NULL,
  [Path]                NVARCHAR(MAX)  NULL,
  [Position]            BIGINT         NULL,
  [OriginalPosition]    BIGINT         NULL,
  [CommitId]            NVARCHAR(200)  NULL,
  [OriginalCommitId]    NVARCHAR(200)  NULL,
  [InReplyTo]           BIGINT         NULL,
  [Body]                NVARCHAR(MAX)  NOT NULL,
  [CreatedAt]           DATETIMEOFFSET NOT NULL,
  [UpdatedAt]           DATETIMEOFFSET NOT NULL,
  CONSTRAINT [PK_PullRequestComments] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_PullRequestComments_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues]([Id]),
  CONSTRAINT [FK_PullRequestComments_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_PullRequestComments_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts]([Id]),
  CONSTRAINT [FK_PullRequestComments_PullRequestReviewId_Reviews_Id] FOREIGN KEY ([PullRequestReviewId]) REFERENCES [dbo].[Reviews]([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_PullRequestComments_IssueId] ON [dbo].[PullRequestComments]([IssueId])
GO

CREATE NONCLUSTERED INDEX [IX_PullRequestComments_RepositoryId] ON [dbo].[PullRequestComments]([RepositoryId])
GO

CREATE NONCLUSTERED INDEX [IX_PullRequestComments_UserId] ON [dbo].[PullRequestComments]([UserId])
GO

CREATE NONCLUSTERED INDEX [IX_PullRequestComments_PullRequestReviewId]
  ON [dbo].[PullRequestComments]([PullRequestReviewId])
  WHERE [PullRequestReviewId] IS NOT NULL
GO
