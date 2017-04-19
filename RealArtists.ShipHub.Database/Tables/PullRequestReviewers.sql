CREATE TABLE [dbo].[PullRequestReviewers] (
  [IssueId] BIGINT NOT NULL,
  [UserId]  BIGINT NOT NULL,
  CONSTRAINT [PK_PullRequestReviewers] PRIMARY KEY CLUSTERED ([IssueId], [UserId]),
  CONSTRAINT [FK_PullRequestReviewers_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues] ([Id]),
  CONSTRAINT [FK_PullRequestReviewers_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_PullRequestReviewers_UserId] ON [dbo].[PullRequestReviewers]([UserId])
GO
