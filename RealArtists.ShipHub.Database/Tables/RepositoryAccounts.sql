CREATE TABLE [dbo].[RepositoryAccounts] (
  [RepositoryId] INT NOT NULL,
  [AccountId]    INT NOT NULL,
  CONSTRAINT [PK_RepositoryAccounts] PRIMARY KEY CLUSTERED ([RepositoryId], [AccountId]),
  CONSTRAINT [FK_RepositoryAccounts_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_RepositoryAccounts_REpositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_RepositoryAccounts_AccountId] ON [dbo].[RepositoryAccounts]([AccountId]);
GO

CREATE NONCLUSTERED INDEX [IX_RepositoryAccounts_RepositoryId] ON [dbo].[RepositoryAccounts]([RepositoryId]);
GO
