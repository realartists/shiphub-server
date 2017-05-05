CREATE TABLE [dbo].[Reactions]  (
  [Id]                   BIGINT         NOT NULL,
  [UserId]               BIGINT         NOT NULL,
  [IssueId]              BIGINT         NULL,
  [CommentId]            BIGINT         NULL,
  [PullRequestCommentId] BIGINT         NULL,
  [Content]              NVARCHAR(15)   NOT NULL,
  [CreatedAt]            DATETIMEOFFSET NOT NULL,
  CONSTRAINT [CK_Reactions_IssueOrCommentOrPullRequestExclusive] CHECK (
    1 = (CASE WHEN IssueId IS NULL THEN 0 ELSE 1 END +
         CASE WHEN CommentId IS NULL THEN 0 ELSE 1 END +
         CASE WHEN PullRequestCommentId IS NULL THEN 0 ELSE 1 END)),
  CONSTRAINT [PK_Reactions] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Reactions_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Reactions_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues] ([Id]),
  CONSTRAINT [FK_Reactions_CommentId_Comments_Id] FOREIGN KEY ([CommentId]) REFERENCES [dbo].[Comments] ([Id]),
  CONSTRAINT [FK_Reactions_PullRequestCommentId_PullRequestComments_Id] FOREIGN KEY ([PullRequestCommentId]) REFERENCES [dbo].[PullRequestComments] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_Reactions_UserId] ON [dbo].[Reactions]([UserId])
GO

CREATE NONCLUSTERED INDEX [IX_Reactions_IssueId]
  ON [dbo].[Reactions]([IssueId])
  WHERE ([IssueId] IS NOT NULL)
GO

CREATE NONCLUSTERED INDEX [IX_Reactions_CommentId]
  ON [dbo].[Reactions]([CommentId])
  WHERE ([CommentId] IS NOT NULL)
GO

CREATE NONCLUSTERED INDEX [IX_Reactions_PullRequestCommentId]
  ON [dbo].[Reactions]([PullRequestCommentId])
  WHERE ([PullRequestCommentId] IS NOT NULL)
GO
