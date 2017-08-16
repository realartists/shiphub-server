CREATE TABLE [dbo].[IssueMentions] (
  [IssueId] BIGINT NOT NULL,
  [UserId]  BIGINT NOT NULL,
  CONSTRAINT [PK_IssueMentions] PRIMARY KEY CLUSTERED ([IssueId], [UserId]),
  -- NOTE! Can't FK to issues here, because we need to track all mentions for a user, even in repos not currently synced.
  CONSTRAINT [FK_IssueMentions_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_IssueMentions_UserId] ON [dbo].[IssueMentions]([UserId])
GO
