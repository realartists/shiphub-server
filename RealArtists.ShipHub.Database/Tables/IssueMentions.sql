CREATE TABLE [dbo].[IssueMentions] (
  [IssueId] BIGINT NOT NULL,
  [UserId]  BIGINT NOT NULL,
  CONSTRAINT [PK_IssueMentions] PRIMARY KEY CLUSTERED ([IssueId], [UserId]),
  CONSTRAINT [FK_IssueMentions_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues] ([Id]),
  CONSTRAINT [FK_IssueMentions_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_IssueMentions_UserId] ON [dbo].[IssueMentions]([UserId])
GO
