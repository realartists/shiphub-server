CREATE TABLE [dbo].[Repositories] (
  [Id]             INT            NOT NULL,
  [AccountId]      INT            NOT NULL,
  [Private]        BIT            NOT NULL,
  [HasIssues]      BIT            NOT NULL,
  [Name]           NVARCHAR(100)  NOT NULL,
  [FullName]       NVARCHAR(255)  NOT NULL,
  [Description]    NVARCHAR(500)  NULL,
  [ExtensionJson]  NVARCHAR(MAX)  NULL,
  [RowVersion]     BIGINT         NULL,
  [RestoreVersion] BIGINT         NULL,
  CONSTRAINT [PK_Repositories] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FK_Repositories_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Repositories_RowVersion] ON [dbo].[Repositories]([RowVersion]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Repositories_FullName] ON [dbo].[Repositories]([FullName]);
GO

CREATE TRIGGER [dbo].[TRG_Repositories_Version]
ON [dbo].[Repositories]
AFTER INSERT, UPDATE
NOT FOR REPLICATION
AS 
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

   UPDATE Repositories SET
    [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
  WHERE Id IN (SELECT Id FROM inserted)
END
GO
