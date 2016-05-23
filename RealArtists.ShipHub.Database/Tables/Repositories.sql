CREATE TABLE [dbo].[Repositories] (
  [Id]                   BIGINT         NOT NULL,
  [AccountId]            BIGINT         NOT NULL,
  [Private]              BIT            NOT NULL,
  [Name]                 NVARCHAR(100)  NOT NULL,
  [FullName]             NVARCHAR(255)  NOT NULL,
  [Date]                 DATETIMEOFFSET NOT NULL,
  [AssignableMetaDataId] BIGINT         NULL,
  [LabelMetaDataId]      BIGINT         NULL,
  [RowVersion]           BIGINT         NULL,
  CONSTRAINT [PK_Repositories] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FK_Repositories_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Repositories_AssignableMetaDataId_GitHubMetaData_Id] FOREIGN KEY ([AssignableMetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
  CONSTRAINT [FK_Repositories_LabeleMetaDataId_GitHubMetaData_Id] FOREIGN KEY ([LabelMetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Repositories_FullName] ON [dbo].[Repositories]([FullName]);
GO

CREATE NONCLUSTERED INDEX [IX_Repositories_RowVersion] ON [dbo].[Repositories]([RowVersion]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_Repositories_AssignableMetaDataId]
  ON [dbo].[Repositories]([AssignableMetaDataId])
  WHERE ([AssignableMetaDataId] IS NOT NULL);
GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_Repositories_LabelMetaDataId]
  ON [dbo].[Repositories]([LabelMetaDataId])
  WHERE ([LabelMetaDataId] IS NOT NULL);
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
