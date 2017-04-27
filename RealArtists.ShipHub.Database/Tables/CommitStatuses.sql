CREATE TABLE [dbo].[CommitStatuses](
  [Id]           BIGINT         NOT NULL,
  [RepositoryId] BIGINT         NOT NULL,
  [Reference]    NVARCHAR(MAX)  NOT NULL,
  [CreatorId]    BIGINT         NOT NULL,
  [State]        NVARCHAR(MAX)  NULL,
  [TargetUrl]    NVARCHAR(MAX)  NULL,
  [Description]  NVARCHAR(MAX)  NULL,
  [Context]      NVARCHAR(MAX)  NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL,
  CONSTRAINT [PK_CommitStatuses] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_CommitStatuses_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_CommitStatuses_CreatorId_Accounts_Id] FOREIGN KEY ([CreatorId]) REFERENCES [dbo].[Accounts]([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_CommitStatuses_RepositoryId] ON [dbo].[CommitStatuses]([RepositoryId])
GO

CREATE NONCLUSTERED INDEX [IX_CommitStatuses_UserId] ON [dbo].[CommitStatuses]([CreatorId])
GO
