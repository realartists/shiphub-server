CREATE TABLE [dbo].[Issues] (
  [Id]           BIGINT         NOT NULL,
  [UserId]       BIGINT         NOT NULL,
  [RepositoryId] BIGINT         NOT NULL,
  [Number]       INT            NOT NULL,
  [State]        NVARCHAR(6)    NOT NULL,
  [Title]        NVARCHAR(MAX)  NOT NULL,
  [Body]         NVARCHAR(MAX)  NULL,
  [MilestoneId]  BIGINT         NULL,
  [Locked]       BIT            NOT NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL,
  [ClosedAt]     DATETIMEOFFSET NULL,
  [ClosedById]   BIGINT         NULL,
  [PullRequest]  BIT            NOT NULL,
  CONSTRAINT [PK_Issues] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Issues_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Issues_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
  CONSTRAINT [FK_Issues_MilestoneId_Milestones_Id] FOREIGN KEY ([MilestoneId]) REFERENCES [dbo].[Milestones] ([Id]),
  CONSTRAINT [FK_Issues_ClosedById_Accounts_Id] FOREIGN KEY ([ClosedById]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_Issues_UserId] ON [dbo].[Issues]([UserId])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Issues_RepositoryId] ON [dbo].[Issues]([RepositoryId], [Number])
GO

CREATE NONCLUSTERED INDEX [IX_Issues_MilestoneId] ON [dbo].[Issues]([MilestoneId])
GO

CREATE NONCLUSTERED INDEX [IX_Issues_ClosedById] ON [dbo].[Issues]([ClosedById])
GO
