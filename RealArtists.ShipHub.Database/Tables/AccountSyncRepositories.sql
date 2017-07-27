CREATE TABLE [dbo].[AccountSyncRepositories] (
  [AccountId]        BIGINT        NOT NULL,
  [RepositoryId]     BIGINT        NOT NULL,
  [RepoMetadataJson] NVARCHAR(MAX) NULL,
  CONSTRAINT [PK_AccountSyncRepositories] PRIMARY KEY CLUSTERED ([AccountId], [RepositoryId]),
  CONSTRAINT [FK_AccountSyncRepositories_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_AccountSyncRepositories_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_AccountSyncRepositories_AccountId] ON [dbo].[AccountSyncRepositories]([AccountId])
GO

CREATE NONCLUSTERED INDEX [IX_AccountSyncRepositories_RepositoryId] ON [dbo].[AccountSyncRepositories]([RepositoryId])
GO
