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

CREATE TRIGGER [dbo].[TRG_RepositoryAccounts_Version]
ON [dbo].[RepositoryAccounts]
AFTER INSERT, UPDATE, DELETE
NOT FOR REPLICATION
AS 
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  UPDATE Accounts
    SET [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
    WHERE Id IN (
      SELECT AccountId FROM inserted
      UNION
      SELECT AccountId FROM deleted)

  UPDATE Repositories
    SET [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
    WHERE Id IN (
      SELECT RepositoryId FROM inserted
      UNION
      SELECT RepositoryId FROM deleted)
END
GO

