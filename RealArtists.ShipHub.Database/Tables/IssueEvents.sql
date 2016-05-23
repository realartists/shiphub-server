CREATE TABLE [dbo].[IssueEvents] (
  [Id]                   BIGINT         NOT NULL,
  [RepositoryId]         BIGINT         NOT NULL,
  --[ActorId]              BIGINT         NOT NULL,
  --[AssigneeId]           BIGINT         NOT NULL,
  --[CommitId]             NVARCHAR(40)   NULL,
  --[Type]                 NVARCHAR(64)   NOT NULL,
  [CreatedAt]            DATETIMEOFFSET NOT NULL,
  ---- Label
  --[LabelColor]           NVARCHAR(10)   NULL,
  --[LabelName]            NVARCHAR(150)  NULL,
  ---- Milestone
  --[MilestoneId]          BIGINT         NULL,
  --[MilestoneNumber]      INT            NULL,
  --[MilestoneState]       NVARCHAR(10)   NULL,
  --[MilestoneTitle]       NVARCHAR(255)  NULL,
  --[MilestoneDescription] NVARCHAR(255)  NULL,
  --[MilestoneCreatedAt]   DATETIMEOFFSET NULL,
  --[MilestoneUpdatedAt]   DATETIMEOFFSET NULL,
  --[MilestoneClosedAt]    DATETIMEOFFSET NULL,
  --[MilestoneDueOn]       DATETIMEOFFSET NULL,
  ---- Rename
  --[RenameFrom]           NVARCHAR(255)  NULL,
  --[RenameTo]             NVARCHAR(255)  NULL,
  [ExtensionData]        NVARCHAR(MAX)  NOT NULL,
  CONSTRAINT [PK_Events] PRIMARY KEY CLUSTERED ([Id]),
  --CONSTRAINT [FK_Events_ActorId_Accounts_Id] FOREIGN KEY ([ActorId]) REFERENCES [dbo].[Accounts] ([Id]),
  --CONSTRAINT [FK_Events_AssigneeId_Accounts_Id] FOREIGN KEY ([AssigneeId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Events_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
  --CONSTRAINT [FK_Events_MilestoneId_Milestones_Id] FOREIGN KEY ([MilestoneId]) REFERENCES [dbo].[Milestones] ([Id]),
);
GO

--CREATE NONCLUSTERED INDEX [IX_Events_ActorId] ON [dbo].[IssueEvents]([ActorId]);
--GO

--CREATE NONCLUSTERED INDEX [IX_Events_AssigneeId] ON [dbo].[IssueEvents]([AssigneeId]);
--GO

CREATE NONCLUSTERED INDEX [IX_Events_RepositoryId_CreatedAt] ON [dbo].[IssueEvents]([RepositoryId], [CreatedAt]);
GO

--CREATE NONCLUSTERED INDEX [IX_Events_MilestoneId] ON [dbo].[IssueEvents]([MilestoneId]);
--GO
