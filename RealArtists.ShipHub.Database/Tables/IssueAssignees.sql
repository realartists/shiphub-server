CREATE TABLE [dbo].[IssueAssignees] (
  [IssueId] BIGINT NOT NULL,
  [UserId]  BIGINT NOT NULL,
  CONSTRAINT [PK_IssueAssignees] PRIMARY KEY CLUSTERED ([IssueId], [UserId]),
  CONSTRAINT [FK_IssueAssignees_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues] ([Id]),
  CONSTRAINT [FK_IssueAssignees_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_IssueAssignees_UserId] ON [dbo].[IssueAssignees]([UserId])
GO
