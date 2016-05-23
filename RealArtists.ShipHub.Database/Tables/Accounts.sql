CREATE TABLE [dbo].[Accounts] (
  [Id]                   BIGINT         NOT NULL,
  [Type]                 NVARCHAR(4)    NOT NULL,
  [Login]                NVARCHAR(255)  NOT NULL,
  [Date]                 DATETIMEOFFSET NOT NULL,
  [RepositoryMetaDataId] BIGINT         NULL,
  [RowVersion]           BIGINT         NULL,
  CONSTRAINT [PK_Accounts] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Accounts_RepositoryMetaDataId_GitHubMetaData_Id] FOREIGN KEY ([RepositoryMetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_Accounts_Type] ON [dbo].[Accounts]([Type]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_Login] ON [dbo].[Accounts]([Login]);
GO

CREATE NONCLUSTERED INDEX [IX_Accounts_RowVersion] ON [dbo].[Accounts]([RowVersion])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_RepositoryMetaDataId]
  ON [dbo].[Accounts]([RepositoryMetaDataId])
  WHERE ([RepositoryMetaDataId] IS NOT NULL);
GO

CREATE TRIGGER [dbo].[TRG_Accounts_Version]
ON [dbo].[Accounts]
AFTER INSERT, UPDATE
NOT FOR REPLICATION
AS 
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  UPDATE Accounts SET
    [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
  WHERE Id IN (SELECT Id FROM inserted)
END
GO
