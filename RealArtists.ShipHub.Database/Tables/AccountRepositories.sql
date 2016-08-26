CREATE TABLE [dbo].[AccountRepositories] (
  [AccountId]    BIGINT NOT NULL,
  [RepositoryId] BIGINT NOT NULL,
  [Hidden]       BIT    NOT NULL,
  [Admin]        BIT    NOT NULL,
  CONSTRAINT [PK_AccountRepositories] PRIMARY KEY CLUSTERED ([AccountId], [RepositoryId]),
  CONSTRAINT [FK_AccountRepositories_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_AccountRepositories_REpositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_AccountRepositories_AccountId] ON [dbo].[AccountRepositories]([AccountId])
GO

CREATE NONCLUSTERED INDEX [IX_AccountRepositories_RepositoryId] ON [dbo].[AccountRepositories]([RepositoryId])
GO
