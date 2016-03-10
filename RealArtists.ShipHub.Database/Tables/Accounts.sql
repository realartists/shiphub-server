CREATE TABLE [dbo].[Accounts] (
  [Id]             INT            NOT NULL,
  [Type]           NVARCHAR(4)    NOT NULL,
  [AvatarUrl]      NVARCHAR(500)  NULL,
  [Login]          NVARCHAR(255)  NOT NULL,
  [Name]           NVARCHAR(255)  NULL,
  [ETag]           NVARCHAR(64)   NULL,
  [Expires]        DATETIMEOFFSET NULL,
  [LastModified]   DATETIMEOFFSET NULL,
  [LastRefresh]    DATETIMEOFFSET NOT NULL,
  [RowVersion]     BIGINT         NULL,
  [RestoreVersion] BIGINT         NULL,
  CONSTRAINT [PK_Accounts] PRIMARY KEY CLUSTERED ([Id] ASC),
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_RowVersion] ON [dbo].[Accounts]([RowVersion]);
GO

CREATE NONCLUSTERED INDEX [UIX_Accounts_Type] ON [dbo].[Accounts]([Type]);
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

  DECLARE @Versions TABLE (
    [Id] INT PRIMARY KEY NOT NULL,
    [RowVersion] BIGINT NULL)

  INSERT INTO @Versions
  SELECT i.Id, IIF(i.[RowVersion] IS NULL, i.RestoreVersion, NULL)
  FROM inserted as i

  UPDATE @Versions SET
    [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
  WHERE [RowVersion] IS NULL

  UPDATE Accounts SET
    [RowVersion] = v.[RowVersion],
    [RestoreVersion] = NULL
  FROM Accounts as t
    INNER JOIN @Versions as v ON (v.Id = t.Id)
END
GO
