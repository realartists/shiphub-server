CREATE TABLE [dbo].[IssueEventAccess] (
  [IssueEventId] BIGINT NOT NULL,
  [UserId]       BIGINT NOT NULL,
  CONSTRAINT [PK_IssueEventAccess] PRIMARY KEY CLUSTERED ([IssueEventId], [UserId]),
  CONSTRAINT [FK_IssueEventAccess_IssueEventId_IssueEvents_Id] FOREIGN KEY ([IssueEventId]) REFERENCES [dbo].[IssueEvents] ([Id]),
  CONSTRAINT [FK_IssueEventAccess_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_IssueEventAccess_UserId]
  ON [dbo].[IssueEventAccess]([UserId])
  INCLUDE (IssueEventId)
GO
