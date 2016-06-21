CREATE TABLE [dbo].[IssueEvents] (
  [Id]            BIGINT         NOT NULL,
  [RepositoryId]  BIGINT         NOT NULL,
  [IssueId]       BIGINT         NOT NULL,
  [ActorId]       BIGINT         NOT NULL,
  [CommitId]      NVARCHAR(40)   NULL,
  [Event]         NVARCHAR(64)   NOT NULL,
  [CreatedAt]     DATETIMEOFFSET NOT NULL,
  [AssigneeId]    BIGINT         NULL,
  [MilestoneId]   BIGINT         NULL,
  [ExtensionData] NVARCHAR(MAX)  NOT NULL,
  CONSTRAINT [PK_IssueEventsEvents] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_IssueEventsEvents_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
  -- Don't enforce these foreign keys because they may reference deleted items
  --CONSTRAINT [FK_IssueEventsEvents_ActorId_Accounts_Id] FOREIGN KEY ([ActorId]) REFERENCES [dbo].[Accounts] ([Id]),
  --CONSTRAINT [FK_IssueEventsEvents_AssigneeId_Accounts_Id] FOREIGN KEY ([AssigneeId]) REFERENCES [dbo].[Accounts] ([Id]),
  --CONSTRAINT [PK_IssueEvents_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues] ([Id]),
  --CONSTRAINT [FK_IssueEventsEvents_MilestoneId_Milestones_Id] FOREIGN KEY ([MilestoneId]) REFERENCES [dbo].[Milestones] ([Id]),
);
GO

--CREATE NONCLUSTERED INDEX [IX_IssueEvents_ActorId] ON [dbo].[IssueEvents]([ActorId]);
--GO

--CREATE NONCLUSTERED INDEX [IX_IssueEvents_AssigneeId] ON [dbo].[IssueEvents]([AssigneeId]);
--GO

--CREATE NONCLUSTERED INDEX [IX_IssueEvents_RepositoryId_CreatedAt] ON [dbo].[IssueEvents]([RepositoryId], [CreatedAt]);
--GO

--CREATE NONCLUSTERED INDEX [IX_IssueEvents_IssueId] ON [dbo].[IssueEvents]([IssueId]);
--GO

--CREATE NONCLUSTERED INDEX [IX_IssueEvents_MilestoneId] ON [dbo].[IssueEvents]([MilestoneId]);
--GO
