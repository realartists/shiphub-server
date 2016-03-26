CREATE TABLE [dbo].[AccountRepositories] (
  [AccountId]    INT NOT NULL,
  [RepositoryId] INT NOT NULL,
  CONSTRAINT [PK_AccountRepositories] PRIMARY KEY CLUSTERED ([AccountId], [RepositoryId]),
  CONSTRAINT [FK_AccountRepositories_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_AccountRepositories_REpositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
);
GO

CREATE TRIGGER [dbo].[TRG_AccountRepositories_Version]
ON [dbo].[AccountRepositories]
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
