CREATE TABLE [dbo].[Accounts] (
  [Id]             INT           NOT NULL,
  [Type]           NVARCHAR(4)   NOT NULL,
  [AvatarUrl]      NVARCHAR(500) NULL,
  [Login]          NVARCHAR(255) NOT NULL,
  [Name]           NVARCHAR(255) NULL,
  [MetaDataId]     BIGINT        NULL,
  [RepoMetaDataId] BIGINT        NULL,
  [ExtensionJson]  NVARCHAR(MAX) NULL,
  [RowVersion]     BIGINT        NULL,
  CONSTRAINT [PK_Accounts] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Accounts_MetaDataId_GitHubMetaData_Id] FOREIGN KEY ([MetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
  CONSTRAINT [FK_Accounts_RepoMetaDataId_GitHubMetaData_Id] FOREIGN KEY ([RepoMetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_Accounts_Type] ON [dbo].[Accounts]([Type]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_Login] ON [dbo].[Accounts]([Login]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_RowVersion] ON [dbo].[Accounts]([RowVersion]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_Accounts_MetaDataId] ON [dbo].[Accounts]([MetaDataId]);
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
