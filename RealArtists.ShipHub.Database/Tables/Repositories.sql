CREATE TABLE [dbo].[Repositories] (
  [Id]             INT            NOT NULL,
  [AccountId]         INT            NOT NULL,
  [Private]        BIT            NOT NULL,
  [HasIssues]      BIT            NOT NULL,
  [Name]           NVARCHAR(100)  NOT NULL,
  [FullName]       NVARCHAR(500)  NOT NULL,
  [Description]    NVARCHAR(500)  NULL,
  [ETag]           NVARCHAR(64)   NULL,
  [Expires]        DATETIMEOFFSET NULL,
  [LastModified]   DATETIMEOFFSET NULL,
  [LastRefresh]    DATETIMEOFFSET NOT NULL,
  [ExtensionJson]  NVARCHAR(MAX)  NULL,
  [RowVersion]     BIGINT         NULL,
  [RestoreVersion] BIGINT         NULL,
  CONSTRAINT [PK_Repositories] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FKCD_Repositories_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]) ON DELETE CASCADE
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Repositories_RowVersion] ON [dbo].[Repositories]([RowVersion]);
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

  DECLARE @Versions TABLE (
    [Id] INT PRIMARY KEY NOT NULL,
    [RowVersion] BIGINT NULL)

  INSERT INTO @Versions
  SELECT i.Id, IIF(i.[RowVersion] IS NULL, i.RestoreVersion, NULL)
  FROM inserted as i

  UPDATE @Versions SET
    [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
  WHERE [RowVersion] IS NULL

  UPDATE Repositories SET
    [RowVersion] = v.[RowVersion],
    [RestoreVersion] = NULL
  FROM Repositories as t
    INNER JOIN @Versions as v ON (v.Id = t.Id)
END
GO